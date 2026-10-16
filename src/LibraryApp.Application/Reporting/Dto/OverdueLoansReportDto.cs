using LibraryApp.Application.Common;

namespace LibraryApp.Application.Reporting.Dto;

public sealed record OverdueLoansReportDto(
    DateOnly AsOfDate,
    DateTime GeneratedAtUtc,
    Guid? DepartmentId,
    int? MinimumDaysOverdue,
    int? MaximumDaysOverdue,
    IReadOnlyList<OverdueLoanItemDto> Items,
    int TotalCount = 0,
    int Page = 1,
    int PageSize = 20)
{
    public int TotalPages =>
        PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

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
