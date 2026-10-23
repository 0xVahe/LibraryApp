namespace LibraryApp.Application.Reporting.Dto;

/// <summary>REP-04: loans of a period by department; every department is listed, also without loans.</summary>
/// <param name="PeriodStart">The first day of the period (Asia/Yerevan), included.</param>
/// <param name="PeriodEnd">The last day of the period (Asia/Yerevan), included.</param>
/// <param name="GeneratedAtUtc">When the report was made.</param>
/// <param name="Departments">Every department, by name.</param>
public sealed record ReadingActivityByDepartmentReportDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTime GeneratedAtUtc,
    IReadOnlyList<DepartmentReadingActivityDto> Departments);

/// <summary>The loans of one department in the period (REP-04).</summary>
/// <param name="DepartmentId">Department id.</param>
/// <param name="Department">Department name.</param>
/// <param name="Employees">Staff members of the department who have not left the bank.</param>
/// <param name="LoanCount">Loans issued in the period to the staff of the department.</param>
/// <param name="LoansPer10Employees">Loans per 10 employees, with one decimal; 0 without employees.</param>
/// <param name="TopCategoryId">The category with the most loans, or null without loans.</param>
/// <param name="TopCategory">The name of that category, or null without loans.</param>
/// <param name="TopCategoryLoanCount">The loans of that category; 0 without loans.</param>
public sealed record DepartmentReadingActivityDto(
    Guid DepartmentId,
    string Department,
    int Employees,
    int LoanCount,
    decimal LoansPer10Employees,
    Guid? TopCategoryId,
    string? TopCategory,
    int TopCategoryLoanCount);
