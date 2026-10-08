namespace VacationApp.Core;

public enum UserRole
{
    Employee,
    Leader,
    HumanResources,
    Payroll
}

public enum RequestStatus
{
    PendingLeader,
    PendingHumanResources,
    Approved,
    Rejected,
    RegisteredByPayroll
}

public sealed record UserProfile(
    Guid Id,
    string FullName,
    string Email,
    UserRole Role,
    Guid? LeaderId = null,
    int AnnualAllowanceDays = 20);

public sealed record RequestHistoryEntry(
    DateTimeOffset Timestamp,
    string ActorName,
    UserRole ActorRole,
    string Action,
    string Comment);

public sealed record VacationRequest(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly StartDate,
    DateOnly EndDate,
    int BusinessDays,
    string Reason,
    RequestStatus Status,
    DateTimeOffset SubmittedAt,
    IReadOnlyList<RequestHistoryEntry> History);

public sealed record VacationBalance(int Total, int Used, int Reserved, int Available, int Year);

public sealed record VacationSnapshot(
    IReadOnlyList<UserProfile> Users,
    IReadOnlyList<VacationRequest> Requests);

public sealed class VacationValidationException(string message) : Exception(message);
