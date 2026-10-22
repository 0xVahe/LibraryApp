using LibraryApp.Application.Reporting.Dto;

namespace LibraryApp.Application.Reporting.Ports;

public interface IReportingReadRepository
{
    Task<IReadOnlyList<OverdueLoanData>> GetOverdueLoansAsync(DateTime overdueCutoffUtc, Guid? departmentId, CancellationToken ct = default);
    Task<DashboardCountsData> GetDashboardCountsAsync(DateTime nowUtc, CancellationToken ct = default);
    Task<IReadOnlyList<TopBookReportItemDto>> GetTopBooksAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<IReadOnlyList<TopCategoryReportItemDto>> GetTopCategoriesAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<IReadOnlyList<DepartmentEmployeesData>> GetDepartmentEmployeesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DepartmentCategoryLoansData>> GetDepartmentCategoryLoansAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}

/// <summary>REP-04: every department with its employees who have not left the bank.</summary>
public sealed record DepartmentEmployeesData(Guid DepartmentId, string Department, int Employees);

/// <summary>REP-04: loans issued in the period, counted per department of the borrower and book category.</summary>
public sealed record DepartmentCategoryLoansData(Guid DepartmentId, Guid CategoryId, string Category, int LoanCount);

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

public sealed record DashboardCountsData(
    int TotalBooks,
    int TotalCopies,
    int ActiveReaders,
    int ActiveLoans,
    int TotalLoans,
    int OverdueLoans,
    int RestrictedEmployees,
    int SuspendedEmployees);
