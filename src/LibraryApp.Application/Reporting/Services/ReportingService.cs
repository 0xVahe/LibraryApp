using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Ports;
using Microsoft.Extensions.Caching.Memory;

namespace LibraryApp.Application.Reporting.Services;

public sealed class ReportingService(
    IReportingReadRepository repository,
    IClock clock,
    IWorkingDayCalendar workingDayCalendar,
    IMemoryCache cache) : IReportingService
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

    public async Task<DashboardReportDto> GetDashboardAsync(CancellationToken ct = default)
    {
        DateTime nowUtc = clock.UtcNow;
        DateOnly today = clock.LocalToday;
        string cacheKey = $"reporting:dashboard:{today:yyyy-MM-dd}";
        if (cache.TryGetValue(cacheKey, out DashboardReportDto? cached) && cached is not null)
            return cached;

        DashboardCountsData counts = await repository.GetDashboardCountsAsync(nowUtc, ct);
        decimal percentage = counts.TotalLoans == 0
            ? 0m
            : Math.Round((decimal)counts.OverdueLoans * 100m / counts.TotalLoans, 1, MidpointRounding.AwayFromZero);

        DashboardReportDto report = new(
            today,
            nowUtc,
            counts.TotalBooks,
            counts.TotalCopies,
            counts.ActiveReaders,
            counts.ActiveLoans,
            counts.OverdueLoans,
            percentage,
            counts.RestrictedEmployees,
            counts.SuspendedEmployees);

        cache.Set(cacheKey, report, TimeSpan.FromMinutes(5));
        return report;
    }

    public async Task<TopBooksAndCategoriesReportDto> GetTopBooksAndCategoriesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (from > to)
            throw new ArgumentException("The start date must be on or before the end date.");

        DateTime fromUtc = clock.StartOfLocalDayUtc(from);
        DateTime toUtc = clock.EndOfLocalDayUtc(to);
        IReadOnlyList<TopBookReportItemDto> books = await repository.GetTopBooksAsync(fromUtc, toUtc, ct);
        IReadOnlyList<TopCategoryReportItemDto> categories = await repository.GetTopCategoriesAsync(fromUtc, toUtc, ct);

        return new TopBooksAndCategoriesReportDto(from, to, clock.UtcNow, books, categories);
    }
}
