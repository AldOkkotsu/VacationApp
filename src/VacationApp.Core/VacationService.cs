using System.Security.Cryptography;
using System.Text;

namespace VacationApp.Core;

/// <summary>
/// Local demonstration of a vacation workflow. The shared password and session
/// are for development only; a production app needs server-side authentication,
/// authorization and persistence. Register this service as a singleton.
/// </summary>
public sealed class VacationService
{
    public const string DemoPassword = "Vacaciones2026!";

    private readonly IVacationRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private VacationSnapshot _snapshot;
    private Guid? _sessionUserId;

    public VacationService(IVacationRepository repository, TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? TimeProvider.System;
        var snapshot = _repository.Load();
        _snapshot = Freeze(snapshot ?? CreateDemoSnapshot());
        if (snapshot is null)
            _repository.Save(_snapshot);
    }

    public IReadOnlyList<UserProfile> GetDemoAccounts()
    {
        lock (_gate)
            return _snapshot.Users;
    }

    public UserProfile? SignIn(string email, string password)
    {
        lock (_gate)
        {
            _sessionUserId = null;
            if (string.IsNullOrWhiteSpace(email) || password is null ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(DemoPassword)))
                return null;

            var user = _snapshot.Users.FirstOrDefault(candidate =>
                string.Equals(candidate.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));
            _sessionUserId = user?.Id;
            return user;
        }
    }

    public void SignOut()
    {
        lock (_gate)
            _sessionUserId = null;
    }

    public IReadOnlyList<VacationRequest> GetRequests(UserProfile actor)
    {
        lock (_gate)
        {
            var user = RequireSession(actor);
            return Array.AsReadOnly(_snapshot.Requests
                .Where(request => IsVisible(user, request))
                .OrderByDescending(request => request.SubmittedAt)
                .ThenByDescending(request => request.Id)
                .ToArray());
        }
    }

    public VacationBalance GetBalance(UserProfile actor, int? year = null)
    {
        lock (_gate)
        {
            var user = RequireSession(actor);
            RequireEmployee(user);
            var balanceYear = year ?? _timeProvider.GetLocalNow().Year;
            if (balanceYear is < 1 or > 9999)
                throw new VacationValidationException("El año indicado no es válido.");
            return CalculateBalance(user, balanceYear);
        }
    }

    public VacationRequest SubmitRequest(UserProfile actor, DateOnly startDate,
        DateOnly endDate, string reason)
    {
        lock (_gate)
        {
            var user = RequireSession(actor);
            RequireEmployee(user);
            var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
            if (startDate < today)
                throw new VacationValidationException("La fecha de inicio no puede estar en el pasado.");
            if (endDate < startDate)
                throw new VacationValidationException("La fecha final debe ser igual o posterior a la inicial.");
            var businessDays = CountBusinessDays(startDate, endDate);
            if (businessDays == 0)
                throw new VacationValidationException("Selecciona al menos un día de lunes a viernes.");
            var cleanReason = RequireText(reason, "Indica el motivo de tus vacaciones.", 500);
            if (user.LeaderId is null || !_snapshot.Users.Any(candidate =>
                    candidate.Id == user.LeaderId && candidate.Role == UserRole.Leader))
                throw new VacationValidationException("Tu perfil necesita un líder asignado.");
            if (_snapshot.Requests.Any(request => request.EmployeeId == user.Id &&
                    request.Status != RequestStatus.Rejected &&
                    request.StartDate <= endDate && request.EndDate >= startDate))
                throw new VacationValidationException("Estas fechas coinciden con otra solicitud activa.");

            for (var year = startDate.Year; year <= endDate.Year; year++)
            {
                var daysInYear = CountDaysInYear(startDate, endDate, year);
                if (daysInYear > CalculateBalance(user, year).Available)
                    throw new VacationValidationException($"No tienes saldo suficiente para el año {year}.");
            }

            var now = _timeProvider.GetUtcNow();
            var request = new VacationRequest(Guid.NewGuid(), user.Id, user.FullName,
                startDate, endDate, businessDays, cleanReason, RequestStatus.PendingLeader, now,
                Array.AsReadOnly(new[]
                {
                    new RequestHistoryEntry(now, user.FullName, user.Role, "Solicitud enviada", cleanReason)
                }));
            Persist(_snapshot.Requests.Append(request));
            return request;
        }
    }

    public VacationRequest Approve(UserProfile actor, Guid requestId, string? comment = null)
    {
        lock (_gate)
        {
            var user = RequireSession(actor);
            var request = RequireRequest(requestId);
            RequireReviewer(user, request);
            var status = user.Role == UserRole.Leader
                ? RequestStatus.PendingHumanResources
                : RequestStatus.Approved;
            var action = user.Role == UserRole.Leader ? "Aprobada por líder" : "Aprobada por Recursos Humanos";
            return Transition(request, user, status, action, OptionalComment(comment));
        }
    }

    public VacationRequest Reject(UserProfile actor, Guid requestId, string comment)
    {
        lock (_gate)
        {
            var user = RequireSession(actor);
            var request = RequireRequest(requestId);
            RequireReviewer(user, request);
            var cleanComment = RequireText(comment, "Indica el motivo del rechazo.", 500);
            return Transition(request, user, RequestStatus.Rejected, "Solicitud rechazada", cleanComment);
        }
    }

    public VacationRequest RegisterPayroll(UserProfile actor, Guid requestId, string? comment = null)
    {
        lock (_gate)
        {
            var user = RequireSession(actor);
            var request = RequireRequest(requestId);
            if (user.Role != UserRole.Payroll || request.Status != RequestStatus.Approved)
                throw new VacationValidationException("Nómina sólo puede registrar solicitudes aprobadas por Recursos Humanos.");
            return Transition(request, user, RequestStatus.RegisteredByPayroll,
                "Registrada en nómina", OptionalComment(comment));
        }
    }

    private UserProfile RequireSession(UserProfile actor)
    {
        var canonicalUser = actor is null ? null : _snapshot.Users.FirstOrDefault(user => user.Id == actor.Id);
        if (canonicalUser is null || canonicalUser != actor || _sessionUserId != canonicalUser.Id)
            throw new VacationValidationException("Inicia sesión con un perfil válido para continuar.");
        return canonicalUser;
    }

    private static void RequireEmployee(UserProfile user)
    {
        if (user.Role != UserRole.Employee)
            throw new VacationValidationException("Sólo los empleados pueden solicitar vacaciones y consultar su saldo.");
    }

    private VacationRequest RequireRequest(Guid id) =>
        _snapshot.Requests.FirstOrDefault(request => request.Id == id)
        ?? throw new VacationValidationException("La solicitud no existe.");

    private bool IsVisible(UserProfile user, VacationRequest request) => user.Role switch
    {
        UserRole.Employee => request.EmployeeId == user.Id,
        UserRole.Leader => _snapshot.Users.Any(employee => employee.Id == request.EmployeeId && employee.LeaderId == user.Id),
        UserRole.HumanResources => true,
        UserRole.Payroll => request.Status is RequestStatus.Approved or RequestStatus.RegisteredByPayroll,
        _ => false
    };

    private void RequireReviewer(UserProfile user, VacationRequest request)
    {
        var allowed = user.Role switch
        {
            UserRole.Leader => request.Status == RequestStatus.PendingLeader && IsVisible(user, request),
            UserRole.HumanResources => request.Status == RequestStatus.PendingHumanResources,
            _ => false
        };
        if (!allowed)
            throw new VacationValidationException("Tu perfil no puede revisar esta solicitud en su estado actual.");
    }

    private VacationBalance CalculateBalance(UserProfile employee, int year)
    {
        var requests = _snapshot.Requests.Where(request => request.EmployeeId == employee.Id);
        var used = 0;
        var reserved = 0;
        foreach (var request in requests)
        {
            var days = CountDaysInYear(request.StartDate, request.EndDate, year);
            if (request.Status is RequestStatus.Approved or RequestStatus.RegisteredByPayroll)
                used += days;
            else if (request.Status is RequestStatus.PendingLeader or RequestStatus.PendingHumanResources)
                reserved += days;
        }

        return new VacationBalance(employee.AnnualAllowanceDays, used, reserved,
            employee.AnnualAllowanceDays - used - reserved, year);
    }

    private VacationRequest Transition(VacationRequest request, UserProfile actor,
        RequestStatus status, string action, string comment)
    {
        var updated = request with
        {
            Status = status,
            History = Array.AsReadOnly(request.History.Append(new RequestHistoryEntry(
                _timeProvider.GetUtcNow(), actor.FullName, actor.Role, action, comment)).ToArray())
        };
        Persist(_snapshot.Requests.Select(candidate => candidate.Id == request.Id ? updated : candidate));
        return updated;
    }

    private void Persist(IEnumerable<VacationRequest> requests)
    {
        var updated = _snapshot with { Requests = Array.AsReadOnly(requests.ToArray()) };
        // Commit to disk first. A storage failure must not apply an unpersisted mutation.
        _repository.Save(updated);
        _snapshot = updated;
    }

    private static int CountDaysInYear(DateOnly startDate, DateOnly endDate, int year)
    {
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        if (startDate > last || endDate < first)
            return 0;
        return CountBusinessDays(startDate > first ? startDate : first, endDate < last ? endDate : last);
    }

    private static int CountBusinessDays(DateOnly startDate, DateOnly endDate)
    {
        var calendarDays = endDate.DayNumber - startDate.DayNumber + 1;
        var days = calendarDays / 7 * 5;
        for (var offset = 0; offset < calendarDays % 7; offset++)
        {
            var dayOfWeek = (DayOfWeek)(((int)startDate.DayOfWeek + offset) % 7);
            if (dayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                days++;
        }

        return days;
    }

    private static string RequireText(string? value, string emptyMessage, int maximumLength)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
            throw new VacationValidationException(emptyMessage);
        if (text.Length > maximumLength)
            throw new VacationValidationException($"El texto no puede superar los {maximumLength} caracteres.");
        return text;
    }

    private static string OptionalComment(string? comment)
    {
        var text = comment?.Trim() ?? string.Empty;
        if (text.Length > 500)
            throw new VacationValidationException("El comentario no puede superar los 500 caracteres.");
        return text;
    }

    private static VacationSnapshot Freeze(VacationSnapshot snapshot) => new(
        Array.AsReadOnly(snapshot.Users.ToArray()),
        Array.AsReadOnly(snapshot.Requests.Select(request => request with
        {
            History = Array.AsReadOnly(request.History.ToArray())
        }).ToArray()));

    private static VacationSnapshot CreateDemoSnapshot()
    {
        var leaderId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        return new VacationSnapshot(Array.AsReadOnly(new[]
        {
            new UserProfile(Guid.Parse("10000000-0000-0000-0000-000000000001"),
                "Ana García", "ana@vacation.local", UserRole.Employee, leaderId),
            new UserProfile(Guid.Parse("10000000-0000-0000-0000-000000000002"),
                "Luis Hernández", "luis@vacation.local", UserRole.Employee, leaderId),
            new UserProfile(leaderId, "Laura Martínez", "lider@vacation.local", UserRole.Leader),
            new UserProfile(Guid.Parse("30000000-0000-0000-0000-000000000001"),
                "Sofía Ramírez", "rh@vacation.local", UserRole.HumanResources),
            new UserProfile(Guid.Parse("40000000-0000-0000-0000-000000000001"),
                "Carlos López", "nomina@vacation.local", UserRole.Payroll)
        }), Array.Empty<VacationRequest>());
    }
}
