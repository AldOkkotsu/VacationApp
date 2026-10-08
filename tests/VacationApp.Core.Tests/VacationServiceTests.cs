using VacationApp.Core;
using Xunit;

namespace VacationApp.Core.Tests;

public sealed class VacationServiceTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    [Fact]
    public void Login_accepts_normalized_email_and_requires_demo_password()
    {
        var service = CreateService();
        Assert.Null(service.SignIn("ana@vacation.local", "wrong"));
        Assert.Null(service.SignIn("unknown@vacation.local", VacationService.DemoPassword));
        var employee = service.SignIn("  ANA@VACATION.LOCAL  ", VacationService.DemoPassword);
        Assert.NotNull(employee);
        Assert.Equal(UserRole.Employee, employee.Role);
        Assert.Equal(20, service.GetBalance(employee).Available);
    }

    [Fact]
    public void Access_requires_an_active_session_and_the_canonical_profile()
    {
        var service = CreateService();
        var employee = service.GetDemoAccounts().First(user => user.Role == UserRole.Employee);
        Assert.Throws<VacationValidationException>(() => service.GetRequests(employee));
        employee = Login(service, "ana");
        Assert.Throws<VacationValidationException>(() => service.GetRequests(employee with { Role = UserRole.HumanResources }));
        Assert.Throws<VacationValidationException>(() => service.GetBalance(employee with { AnnualAllowanceDays = 500 }));
        var anotherEmployee = service.GetDemoAccounts().Single(user => user.Email == "luis@vacation.local");
        Assert.Throws<VacationValidationException>(() => service.GetRequests(anotherEmployee));
        service.SignOut();
        Assert.Throws<VacationValidationException>(() => service.GetBalance(employee));
    }

    [Fact]
    public void A_failed_login_clears_the_previous_session()
    {
        var service = CreateService();
        var employee = Login(service, "ana");
        Assert.Null(service.SignIn("rh@vacation.local", "wrong"));
        Assert.Throws<VacationValidationException>(() => service.GetRequests(employee));
    }

    [Fact]
    public void Complete_workflow_reserves_then_uses_balance_and_retains_audit_history()
    {
        var service = CreateService();
        var employee = Login(service, "ana");
        var request = service.SubmitRequest(employee, Monday, Monday.AddDays(4), "Viaje familiar");
        Assert.Equal(5, request.BusinessDays);
        Assert.Equal(RequestStatus.PendingLeader, request.Status);
        Assert.Equal(new VacationBalance(20, 0, 5, 15, 2026), service.GetBalance(employee));

        request = service.Approve(Login(service, "lider"), request.Id, "Cobertura confirmada");
        Assert.Equal(RequestStatus.PendingHumanResources, request.Status);
        request = service.Approve(Login(service, "rh"), request.Id);
        Assert.Equal(RequestStatus.Approved, request.Status);
        var payroll = Login(service, "nomina");
        Assert.Single(service.GetRequests(payroll));
        request = service.RegisterPayroll(payroll, request.Id, "Periodo registrado");
        Assert.Equal(RequestStatus.RegisteredByPayroll, request.Status);
        Assert.Equal(new[] { UserRole.Employee, UserRole.Leader, UserRole.HumanResources, UserRole.Payroll },
            request.History.Select(entry => entry.ActorRole));
        Assert.Equal("Periodo registrado", request.History[^1].Comment);
        Assert.Equal(new VacationBalance(20, 5, 0, 15, 2026), service.GetBalance(Login(service, "ana")));
    }

    [Fact]
    public void Employees_see_only_their_requests_and_cannot_approve_them()
    {
        var service = CreateService();
        var employee = Login(service, "ana");
        var request = service.SubmitRequest(employee, Monday, Monday, "Descanso");
        Assert.Throws<VacationValidationException>(() => service.Approve(employee, request.Id));
        var other = Login(service, "luis");
        Assert.Empty(service.GetRequests(other));
        service.SubmitRequest(other, Monday, Monday, "Asunto personal");
        Assert.Single(service.GetRequests(other));
        Assert.Single(service.GetRequests(Login(service, "ana")));
    }

    [Fact]
    public void Leaders_cannot_see_or_review_employees_from_another_team()
    {
        var defaults = CreateService().GetDemoAccounts();
        var foreignLeader = new UserProfile(Guid.NewGuid(), "Otro líder", "otro@vacation.local", UserRole.Leader);
        var service = CreateService(new MemoryVacationRepository(
            new VacationSnapshot(defaults.Append(foreignLeader).ToArray(), Array.Empty<VacationRequest>())));
        var request = service.SubmitRequest(Login(service, "ana"), Monday, Monday, "Descanso");
        var foreignActor = Login(service, "otro");
        Assert.Empty(service.GetRequests(foreignActor));
        Assert.Throws<VacationValidationException>(() => service.Approve(foreignActor, request.Id));
        Assert.Throws<VacationValidationException>(() => service.Reject(foreignActor, request.Id, "Sin cobertura"));
        Assert.Single(service.GetRequests(Login(service, "lider")));
    }

    [Fact]
    public void Review_order_and_single_processing_are_enforced_for_every_role()
    {
        var service = CreateService();
        var request = service.SubmitRequest(Login(service, "ana"), Monday, Monday, "Descanso");
        var hr = Login(service, "rh");
        Assert.Throws<VacationValidationException>(() => service.Approve(hr, request.Id));
        Assert.Throws<VacationValidationException>(() => service.Reject(hr, request.Id, "Sin cobertura"));
        var payroll = Login(service, "nomina");
        Assert.Empty(service.GetRequests(payroll));
        Assert.Throws<VacationValidationException>(() => service.RegisterPayroll(payroll, request.Id));
        var leader = Login(service, "lider");
        service.Approve(leader, request.Id);
        Assert.Throws<VacationValidationException>(() => service.Approve(leader, request.Id));
        hr = Login(service, "rh");
        service.Approve(hr, request.Id);
        Assert.Throws<VacationValidationException>(() => service.Approve(hr, request.Id));
        Assert.Throws<VacationValidationException>(() => service.Reject(hr, request.Id, "Tarde"));
        payroll = Login(service, "nomina");
        service.RegisterPayroll(payroll, request.Id);
        Assert.Throws<VacationValidationException>(() => service.RegisterPayroll(payroll, request.Id));
    }

    [Theory]
    [InlineData("lider", false)]
    [InlineData("rh", true)]
    public void Rejection_requires_a_reason_and_releases_reserved_days(string reviewer, bool leaderApproval)
    {
        var service = CreateService();
        var request = service.SubmitRequest(Login(service, "ana"), Monday, Monday.AddDays(4), "Descanso");
        if (leaderApproval)
            service.Approve(Login(service, "lider"), request.Id);
        var actor = Login(service, reviewer);
        Assert.Throws<VacationValidationException>(() => service.Reject(actor, request.Id, "  "));
        request = service.Reject(actor, request.Id, "  Falta cobertura  ");
        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Equal("Falta cobertura", request.History[^1].Comment);
        Assert.Throws<VacationValidationException>(() => service.Approve(actor, request.Id));
        var employee = Login(service, "ana");
        Assert.Equal(20, service.GetBalance(employee).Available);
        Assert.Equal(0, service.GetBalance(employee).Reserved);
        service.SubmitRequest(employee, Monday, Monday.AddDays(4), "Nueva fecha coordinada");
        Assert.Equal(2, service.GetRequests(employee).Count);
    }

    [Theory]
    [InlineData(7, 9)]
    [InlineData(12, 11)]
    [InlineData(10, 11)]
    public void Requests_reject_past_reversed_and_weekend_only_dates(int startDay, int endDay)
    {
        var service = CreateService();
        var employee = Login(service, "ana");
        Assert.Throws<VacationValidationException>(() => service.SubmitRequest(employee,
            new DateOnly(2026, 10, startDay), new DateOnly(2026, 10, endDay), "Descanso"));
        Assert.Empty(service.GetRequests(employee));
        Assert.Equal(20, service.GetBalance(employee).Available);
    }

    [Fact]
    public void Requests_count_inclusive_weekdays_and_allow_today()
    {
        var service = CreateService();
        var employee = Login(service, "ana");
        var today = service.SubmitRequest(employee, new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 8), "Hoy");
        var request = service.SubmitRequest(employee, new DateOnly(2026, 10, 9), Monday, "Fin de semana extendido");
        Assert.Equal(1, today.BusinessDays);
        Assert.Equal(2, request.BusinessDays);
        Assert.Equal(3, service.GetBalance(employee).Reserved);
    }

    [Fact]
    public void Local_date_and_balance_year_remain_consistent_when_UTC_has_entered_the_next_year()
    {
        var clock = new MexicoBoundaryTimeProvider();
        var service = new VacationService(new MemoryVacationRepository(), clock);
        var employee = Login(service, "ana");
        Assert.Equal(new VacationBalance(20, 0, 0, 20, 2025), service.GetBalance(employee));

        var localToday = new DateOnly(2025, 12, 31);
        var request = service.SubmitRequest(employee, localToday, localToday, "Día local actual");
        Assert.Equal(1, request.BusinessDays);
        Assert.Equal(clock.GetUtcNow(), request.SubmittedAt);
        Assert.Equal(clock.GetUtcNow(), request.History[0].Timestamp);
        Assert.Equal(new VacationBalance(20, 0, 1, 19, 2025), service.GetBalance(employee));
        Assert.Equal(0, service.GetBalance(employee, 2026).Reserved);

        var exception = Assert.Throws<VacationValidationException>(() => service.SubmitRequest(employee,
            localToday.AddDays(-1), localToday.AddDays(-1), "Día local anterior"));
        Assert.Contains("pasado", exception.Message);
        Assert.Single(service.GetRequests(employee));
    }

    [Fact]
    public void Requests_require_a_reason_and_limit_free_text()
    {
        var service = CreateService();
        var employee = Login(service, "ana");
        Assert.Throws<VacationValidationException>(() => service.SubmitRequest(employee, Monday, Monday, "  "));
        Assert.Throws<VacationValidationException>(() => service.SubmitRequest(employee, Monday, Monday, new string('a', 501)));
        var request = service.SubmitRequest(employee, Monday, Monday, " Descanso ");
        Assert.Equal("Descanso", request.Reason);
        var leader = Login(service, "lider");
        Assert.Throws<VacationValidationException>(() => service.Approve(leader, request.Id, new string('a', 501)));
        Assert.Equal(RequestStatus.PendingLeader, service.GetRequests(leader).Single().Status);
    }

    [Fact]
    public void Active_requests_cannot_overlap_even_at_one_shared_endpoint()
    {
        var service = CreateService();
        var employee = Login(service, "ana");
        service.SubmitRequest(employee, Monday, Monday.AddDays(2), "Primera solicitud");
        Assert.Throws<VacationValidationException>(() => service.SubmitRequest(employee,
            Monday.AddDays(2), Monday.AddDays(4), "Fechas repetidas"));
        Assert.Throws<VacationValidationException>(() => service.SubmitRequest(employee,
            Monday.AddDays(1), Monday.AddDays(1), "Dentro del periodo"));
        Assert.Single(service.GetRequests(employee));
    }

    [Fact]
    public void Pending_and_approved_days_both_reduce_the_annual_allowance()
    {
        var service = CreateService();
        var employee = Login(service, "ana");
        var request = service.SubmitRequest(employee, Monday, new DateOnly(2026, 11, 6), "Cuatro semanas");
        Assert.Equal(20, request.BusinessDays);
        Assert.Equal(0, service.GetBalance(employee).Available);
        Assert.Throws<VacationValidationException>(() => service.SubmitRequest(employee,
            new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9), "Día adicional"));
        service.Approve(Login(service, "lider"), request.Id);
        service.Approve(Login(service, "rh"), request.Id);
        employee = Login(service, "ana");
        Assert.Equal(20, service.GetBalance(employee).Used);
        Assert.Throws<VacationValidationException>(() => service.SubmitRequest(employee,
            new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9), "Día adicional"));
    }

    [Fact]
    public void Cross_year_requests_allocate_each_business_day_to_its_own_year()
    {
        var service = CreateService();
        var employee = Login(service, "ana");
        var request = service.SubmitRequest(employee, new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 4), "Año nuevo");
        Assert.Equal(3, request.BusinessDays);
        Assert.Equal(new VacationBalance(20, 0, 1, 19, 2026), service.GetBalance(employee, 2026));
        Assert.Equal(new VacationBalance(20, 0, 2, 18, 2027), service.GetBalance(employee, 2027));
        service.Approve(Login(service, "lider"), request.Id);
        service.Approve(Login(service, "rh"), request.Id);
        employee = Login(service, "ana");
        Assert.Equal(1, service.GetBalance(employee, 2026).Used);
        Assert.Equal(2, service.GetBalance(employee, 2027).Used);
        Assert.Equal(20, service.GetBalance(employee, 2028).Available);
    }

    [Fact]
    public void Cross_year_requests_cannot_exceed_the_following_years_allowance()
    {
        var service = CreateService();
        var employee = Login(service, "ana");
        service.SubmitRequest(employee, new DateOnly(2027, 1, 4), new DateOnly(2027, 1, 29), "Cuatro semanas");
        var exception = Assert.Throws<VacationValidationException>(() => service.SubmitRequest(employee,
            new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1), "Año nuevo"));
        Assert.Contains("2027", exception.Message);
        Assert.Equal(20, service.GetBalance(employee, 2026).Available);
        Assert.Single(service.GetRequests(employee));
    }

    [Theory]
    [InlineData("lider")]
    [InlineData("rh")]
    [InlineData("nomina")]
    public void Administrative_profiles_cannot_submit_requests_or_access_employee_balance(string account)
    {
        var service = CreateService();
        var actor = Login(service, account);
        Assert.Throws<VacationValidationException>(() => service.SubmitRequest(actor, Monday, Monday, "Vacaciones"));
        Assert.Throws<VacationValidationException>(() => service.GetBalance(actor));
    }

    [Fact]
    public async Task Concurrent_requests_in_one_service_cannot_reserve_the_same_day_twice()
    {
        var service = CreateService();
        var employee = Login(service, "ana");
        var attempts = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
        {
            try
            {
                service.SubmitRequest(employee, Monday, Monday, "Descanso");
                return true;
            }
            catch (VacationValidationException)
            {
                return false;
            }
        })));
        Assert.Single(attempts, success => success);
        Assert.Single(service.GetRequests(employee));
        Assert.Equal(1, service.GetBalance(employee).Reserved);
    }

    [Fact]
    public void Json_repository_survives_restart_without_persisting_the_demo_password()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vacation-tests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "vacations.json");
        try
        {
            var service = CreateService(new JsonVacationRepository(filePath));
            var request = service.SubmitRequest(Login(service, "ana"), Monday, Monday.AddDays(4), "Viaje");
            service.Approve(Login(service, "lider"), request.Id, "OK");
            var restored = CreateService(new JsonVacationRepository(filePath));
            var employee = Login(restored, "ana");
            var restoredRequest = Assert.Single(restored.GetRequests(employee));
            Assert.Equal(request.Id, restoredRequest.Id);
            Assert.Equal(RequestStatus.PendingHumanResources, restoredRequest.Status);
            Assert.Equal(2, restoredRequest.History.Count);
            Assert.Equal(5, restored.GetBalance(employee).Reserved);
            Assert.DoesNotContain(VacationService.DemoPassword, File.ReadAllText(filePath));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Storage_failure_does_not_apply_an_in_memory_mutation()
    {
        var repository = new FailingRepository();
        var service = CreateService(repository);
        var employee = Login(service, "ana");
        repository.FailWrites = true;
        Assert.Throws<IOException>(() => service.SubmitRequest(employee, Monday, Monday, "Descanso"));
        Assert.Empty(service.GetRequests(employee));
        Assert.Equal(20, service.GetBalance(employee).Available);
    }

    private static VacationService CreateService(IVacationRepository? repository = null) =>
        new(repository ?? new MemoryVacationRepository(), new FixedTimeProvider());

    private static UserProfile Login(VacationService service, string account) =>
        service.SignIn($"{account}@vacation.local", VacationService.DemoPassword)!;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class MexicoBoundaryTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 2, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone(
            "MexicoTestZone", TimeSpan.FromHours(-6), "México UTC-06", "México UTC-06");
    }

    private sealed class FailingRepository : IVacationRepository
    {
        private VacationSnapshot? _snapshot;
        public bool FailWrites { get; set; }
        public VacationSnapshot? Load() => _snapshot;
        public void Save(VacationSnapshot snapshot)
        {
            if (FailWrites)
                throw new IOException("Simulated disk error.");
            _snapshot = snapshot;
        }
    }
}
