namespace LibraryApp.Application.Reporting.Dto;

public sealed record OverdueLoansReportDto(
    DateOnly AsOfDate,
    DateTime GeneratedAtUtc,
    Guid? DepartmentId,
    int? MinimumDaysOverdue,
    int? MaximumDaysOverdue,
    IReadOnlyList<OverdueLoanItemDto> Items);

public sealed record OverdueLoanItemDto(
    Guid LoanId,
    string EmployeeId,
    string EmployeeName,
    string Department,
    string? LineManagerEmail,
    string BookTitle,
    DateOnly DueDate,
    int WorkingDaysOverdue,
    int PointsSoFar);
