using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Ports;

namespace LibraryApp.Application.Reporting.Services;

public sealed class ReportingService(
    IReportingReadRepository repository,
    IClock clock,
    IWorkingDayCalendar workingDayCalendar) : IReportingService
{
    public async Task<OverdueLoansReportDto> GetOverdueLoansAsync(
        Guid? departmentId,
        int? minimumDaysOverdue,
        int? maximumDaysOverdue,
        CancellationToken ct = default)
    {
        if (minimumDaysOverdue is < 0 || maximumDaysOverdue is < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumDaysOverdue), "Overdue day filters cannot be negative.");

        if (minimumDaysOverdue.HasValue && maximumDaysOverdue.HasValue && minimumDaysOverdue > maximumDaysOverdue)
            throw new ArgumentException("Minimum overdue days cannot exceed maximum overdue days.");

        if (departmentId == Guid.Empty)
            throw new ArgumentException("Department ID cannot be empty.", nameof(departmentId));

        DateTime nowUtc = clock.UtcNow;
        DateOnly today = clock.LocalToday;
        IReadOnlyList<OverdueLoanData> rows = await repository.GetOverdueLoansAsync(nowUtc, ct);

        List<OverdueLoanItemDto> items = rows
            .Where(row => !departmentId.HasValue || row.DepartmentId == departmentId.Value)
            .Select(row => new OverdueLoanItemDto(
                row.LoanId,
                row.EmployeeId,
                row.EmployeeName,
                row.Department,
                row.LineManagerEmail,
                row.BookTitle,
                clock.ToLocalDate(row.DueAtUtc),
                workingDayCalendar.CountWorkingDays(clock.ToLocalDate(row.DueAtUtc), today),
                row.PointsSoFar))
            .Where(item => !minimumDaysOverdue.HasValue || item.WorkingDaysOverdue >= minimumDaysOverdue.Value)
            .Where(item => !maximumDaysOverdue.HasValue || item.WorkingDaysOverdue <= maximumDaysOverdue.Value)
            .OrderByDescending(item => item.WorkingDaysOverdue)
            .ThenBy(item => item.EmployeeName)
            .ToList();

        return new OverdueLoansReportDto(today, nowUtc, departmentId, minimumDaysOverdue, maximumDaysOverdue, items);
    }
}
