using LibraryApp.Application.Common;

namespace LibraryApp.Application.Reporting.Dto;

/// <summary>REP-01: open loans past their due date, most working days overdue first, one page of them.</summary>
/// <param name="AsOfDate">The day of the report (Asia/Yerevan): a loan due before this day is overdue.</param>
/// <param name="GeneratedAtUtc">When the report was made.</param>
/// <param name="DepartmentId">The department filter, or null for every department.</param>
/// <param name="MinimumDaysOverdue">The smallest number of working days overdue asked for, or null.</param>
/// <param name="MaximumDaysOverdue">The largest number of working days overdue asked for, or null.</param>
/// <param name="Items">The overdue loans of this page.</param>
/// <param name="TotalCount">The overdue loans that match the filters, on every page.</param>
/// <param name="Page">The page number, from 1.</param>
/// <param name="PageSize">The loans per page.</param>
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
    /// <summary>The number of pages.</summary>
    public int TotalPages =>
        PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>One overdue loan (REP-01).</summary>
/// <param name="LoanId">Loan id.</param>
/// <param name="EmployeeId">The employee ID of the borrower.</param>
/// <param name="EmployeeName">The full name of the borrower.</param>
/// <param name="Department">The department of the borrower.</param>
/// <param name="LineManagerEmail">The line manager of the borrower, or null when there is none.</param>
/// <param name="BookTitle">The title of the book.</param>
/// <param name="DueDate">The due date (Asia/Yerevan).</param>
/// <param name="WorkingDaysOverdue">Working days since the due date; weekends and holidays do not count.</param>
/// <param name="PointsSoFar">The late points a return today would add, by the penalty policy.</param>
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
