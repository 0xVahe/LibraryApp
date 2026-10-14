using LibraryApp.Application.Reporting.Dto;

namespace LibraryApp.Application.Reporting.Ports;

public interface IReportingReadRepository
{
    Task<IReadOnlyList<OverdueLoanData>> GetOverdueLoansAsync(DateTime nowUtc, CancellationToken ct = default);
}

public sealed record OverdueLoanData(
    Guid LoanId,
    string EmployeeId,
    string EmployeeName,
    string Department,
    Guid DepartmentId,
    string? LineManagerEmail,
    string BookTitle,
    DateTime DueAtUtc,
    int PointsSoFar);
