using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using VacationApp.Core;

namespace VacationApp.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly VacationService _service;
    private bool _isBusy;
    private string _feedback = string.Empty;
    private DateTime? _startDate = DateTime.Today;
    private DateTime? _endDate = DateTime.Today.AddDays(1);
    private string _reason = string.Empty;

    public DashboardViewModel(VacationService service, UserProfile actor)
    {
        _service = service;
        Actor = actor;
        Reload();
    }

    public UserProfile Actor { get; }
    public ObservableCollection<RequestCardViewModel> Requests { get; } = [];
    public string Name => Actor.FullName;
    public string Role => RoleLabel(Actor.Role);
    public bool IsEmployee => Actor.Role == UserRole.Employee;
    public bool IsReviewer => !IsEmployee;
    public string Greeting => IsEmployee ? "Planea tu próximo descanso" : Actor.Role switch
    {
        UserRole.Leader => "Cuida el descanso de tu equipo",
        UserRole.HumanResources => "Revisa y autoriza vacaciones",
        UserRole.Payroll => "Mantén los registros al día",
        _ => "Tus vacaciones, en orden"
    };
    public string RoleDescription => Actor.Role switch
    {
        UserRole.Employee => "Consulta tu saldo, envía una solicitud y sigue su avance.",
        UserRole.Leader => "Revisa las solicitudes de los empleados de tu equipo.",
        UserRole.HumanResources => "Aprueba las solicitudes revisadas por sus líderes.",
        UserRole.Payroll => "Registra en nómina las vacaciones aprobadas por Recursos Humanos.",
        _ => string.Empty
    };
    public string ListTitle => IsEmployee ? "Mis solicitudes" : Actor.Role == UserRole.Leader
        ? "Solicitudes de mi equipo" : "Solicitudes de vacaciones";
    public string ListDescription => Actor.Role switch
    {
        UserRole.Employee => "Cada solicitud pasa por tu líder, Recursos Humanos y Nómina.",
        UserRole.Leader => "Tu aprobación envía la solicitud a Recursos Humanos.",
        UserRole.HumanResources => "Tu aprobación permite que Nómina registre los días.",
        UserRole.Payroll => "El registro confirma que la solicitud se procesó en nómina.",
        _ => string.Empty
    };
    public string EmptyTitle => IsEmployee ? "Tu próximo descanso empieza aquí" : "Aún no hay solicitudes";
    public string EmptyDescription => IsEmployee
        ? "Completa el formulario para enviar tu primera solicitud."
        : "Las solicitudes aparecerán aquí cuando un empleado las envíe.";
    public string BalanceYear { get; private set; } = string.Empty;
    public int AvailableDays { get; private set; }
    public int UsedDays { get; private set; }
    public int ReservedDays { get; private set; }
    public int TotalDays { get; private set; }
    public int PendingCount { get; private set; }
    public int ApprovedCount { get; private set; }
    public int RejectedCount { get; private set; }
    public bool IsEmpty => Requests.Count == 0;
    public string RequestCount => Requests.Count == 1 ? "1 solicitud" : $"{Requests.Count} solicitudes";
    public DateTime MinimumDate => DateTime.Today;

    public DateTime? StartDate
    {
        get => _startDate;
        set { _startDate = value; Notify(); }
    }
    public DateTime? EndDate
    {
        get => _endDate;
        set { _endDate = value; Notify(); }
    }
    public string Reason
    {
        get => _reason;
        set { _reason = value; Notify(); }
    }
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; Notify(); Notify(nameof(IsNotBusy)); }
    }
    public bool IsNotBusy => !IsBusy;
    public string Feedback
    {
        get => _feedback;
        set { _feedback = value; Notify(); Notify(nameof(HasFeedback)); }
    }
    public bool HasFeedback => !string.IsNullOrWhiteSpace(Feedback);

    public void Reload()
    {
        var requests = _service.GetRequests(Actor);
        Requests.Clear();
        foreach (var request in requests.OrderByDescending(request => request.SubmittedAt))
            Requests.Add(new RequestCardViewModel(request, Actor.Role));

        if (IsEmployee)
        {
            var balance = _service.GetBalance(Actor);
            BalanceYear = $"Saldo de vacaciones · {balance.Year}";
            AvailableDays = balance.Available;
            UsedDays = balance.Used;
            ReservedDays = balance.Reserved;
            TotalDays = balance.Total;
        }

        PendingCount = requests.Count(request => Actor.Role switch
        {
            UserRole.Leader => request.Status == RequestStatus.PendingLeader,
            UserRole.HumanResources => request.Status == RequestStatus.PendingHumanResources,
            UserRole.Payroll => request.Status == RequestStatus.Approved,
            _ => request.Status is RequestStatus.PendingLeader or RequestStatus.PendingHumanResources
        });
        ApprovedCount = requests.Count(request => request.Status is RequestStatus.Approved or RequestStatus.RegisteredByPayroll);
        RejectedCount = requests.Count(request => request.Status == RequestStatus.Rejected);
        Notify(string.Empty);
    }

    public static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Employee => "Empleado",
        UserRole.Leader => "Líder",
        UserRole.HumanResources => "Recursos Humanos",
        UserRole.Payroll => "Nómina",
        _ => role.ToString()
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

public sealed class RequestCardViewModel
{
    private static readonly CultureInfo SpanishCulture = CultureInfo.GetCultureInfo("es-MX");
    public RequestCardViewModel(VacationRequest request, UserRole role)
    {
        Request = request;
        CanApprove = (role == UserRole.Leader && request.Status == RequestStatus.PendingLeader)
            || (role == UserRole.HumanResources && request.Status == RequestStatus.PendingHumanResources);
        CanRegisterPayroll = role == UserRole.Payroll && request.Status == RequestStatus.Approved;
    }

    public VacationRequest Request { get; }
    public string EmployeeName => Request.EmployeeName;
    public string Dates => $"{Request.StartDate.ToString("dd MMM yyyy", SpanishCulture)} → {Request.EndDate.ToString("dd MMM yyyy", SpanishCulture)}";
    public string Days => Request.BusinessDays == 1 ? "1 día hábil" : $"{Request.BusinessDays} días hábiles";
    public string Reason => Request.Reason;
    public string Submitted => $"Enviada el {Request.SubmittedAt.ToLocalTime().ToString("dd MMM yyyy", SpanishCulture)}";
    public string Status => Request.Status switch
    {
        RequestStatus.PendingLeader => "Pendiente de líder",
        RequestStatus.PendingHumanResources => "Pendiente de RH",
        RequestStatus.Approved => "Aprobada · pendiente de nómina",
        RequestStatus.Rejected => "Rechazada",
        RequestStatus.RegisteredByPayroll => "Registrada en nómina",
        _ => Request.Status.ToString()
    };
    public Color StatusBackground => Request.Status switch
    {
        RequestStatus.Rejected => Color.FromArgb("#FCECEC"),
        RequestStatus.Approved or RequestStatus.RegisteredByPayroll => Color.FromArgb("#E5F4ED"),
        _ => Color.FromArgb("#FFF2D9")
    };
    public Color StatusForeground => Request.Status switch
    {
        RequestStatus.Rejected => Color.FromArgb("#A3333D"),
        RequestStatus.Approved or RequestStatus.RegisteredByPayroll => Color.FromArgb("#216846"),
        _ => Color.FromArgb("#84530C")
    };
    public bool CanApprove { get; }
    public bool CanReject => CanApprove;
    public bool CanRegisterPayroll { get; }
    public string ApprovalLabel => Request.Status == RequestStatus.PendingLeader ? "Aprobar y enviar a RH" : "Aprobar vacaciones";
    public string LatestComment => Request.History.Skip(1).LastOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Comment))?.Comment ?? string.Empty;
    public bool HasComment => !string.IsNullOrWhiteSpace(LatestComment);
    public string History => string.Join("\n\n", Request.History.Select(entry =>
        $"{entry.Timestamp.ToLocalTime().ToString("dd MMM yyyy · HH:mm", SpanishCulture)}\n{entry.ActorName} · {DashboardViewModel.RoleLabel(entry.ActorRole)}\n{entry.Action}" +
        (string.IsNullOrWhiteSpace(entry.Comment) ? string.Empty : $"\n{entry.Comment}")));
}
