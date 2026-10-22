namespace LibraryApp.Application.Reporting.Dto;

public sealed record ReadingActivityByDepartmentReportDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTime GeneratedAtUtc,
    IReadOnlyList<DepartmentReadingActivityDto> Departments);

public sealed record DepartmentReadingActivityDto(
    Guid DepartmentId,
    string Department,
    int Employees,
    int LoanCount,
    decimal LoansPer10Employees,
    Guid? TopCategoryId,
    string? TopCategory,
    int TopCategoryLoanCount);