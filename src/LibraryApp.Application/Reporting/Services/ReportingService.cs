using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Ports;
using LibraryApp.Application.Common;
using LibraryApp.Application.Reporting.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Reporting;

namespace LibraryApp.Application.Reporting.Services;

public sealed class ReportingService(
    IReportingReadRepository repository,
    IClock clock,
    IWorkingDayCalendar workingDayCalendar,
    IMemoryCache cache,
    PenaltyPolicy penaltyPolicy,
    IOptions<ReportingOptions> options) : IReportingService
{
    public Task<OverdueLoansReportDto> GetOverdueLoansAsync(
        Guid? departmentId,
        int? minimumDaysOverdue,
        int? maximumDaysOverdue,
        CancellationToken ct = default) =>
        GetOverdueLoansAsync(
            departmentId,
            minimumDaysOverdue,
            maximumDaysOverdue,
            new PageRequest(),
            ct);

    public async Task<OverdueLoansReportDto> GetOverdueLoansAsync(
        Guid? departmentId,
        int? minimumDaysOverdue,
        int? maximumDaysOverdue,
        PageRequest paging,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paging);

        if (minimumDaysOverdue is < 0 || maximumDaysOverdue is < 0 ||
            (minimumDaysOverdue.HasValue && maximumDaysOverdue.HasValue &&
             minimumDaysOverdue > maximumDaysOverdue))
        {
            throw new DomainException(ReportingErrors.InvalidOverdueFilter);
        }

        if (departmentId == Guid.Empty)
        {
            throw new DomainException(ReportingErrors.InvalidDepartmentId);
        }

        DateTime nowUtc = clock.UtcNow;
        DateOnly today = clock.LocalToday;
        DateTime overdueCutoffUtc = clock.StartOfLocalDayUtc(today);

        IReadOnlyList<OverdueLoanData> rows =
            await repository.GetOverdueLoansAsync(overdueCutoffUtc, departmentId, ct);

        List<OverdueLoanItemDto> items = rows
            .Select(row =>
            {
                DateOnly dueDate = clock.ToLocalDate(row.DueAtUtc);
                int workingDaysOverdue =
                    workingDayCalendar.CountWorkingDays(dueDate, today);

                return new OverdueLoanItemDto(
                    row.LoanId,
                    row.EmployeeId,
                    row.EmployeeName,
                    row.Department,
                    row.LineManagerEmail,
                    row.BookTitle,
                    dueDate,
                    workingDaysOverdue,
                    penaltyPolicy.CalculateLatePoints(workingDaysOverdue));
            })
            .Where(item => !minimumDaysOverdue.HasValue ||
                           item.WorkingDaysOverdue >= minimumDaysOverdue.Value)
            .Where(item => !maximumDaysOverdue.HasValue ||
                           item.WorkingDaysOverdue <= maximumDaysOverdue.Value)
            .OrderByDescending(item => item.WorkingDaysOverdue)
            .ThenBy(item => item.EmployeeName)
            .ThenBy(item => item.LoanId)
            .ToList();

        int totalCount = items.Count;

        List<OverdueLoanItemDto> pageItems = items
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToList();

        return new OverdueLoansReportDto(
            today,
            nowUtc,
            departmentId,
            minimumDaysOverdue,
            maximumDaysOverdue,
            pageItems,
            totalCount,
            paging.Page,
            paging.PageSize);
    }

    public async Task<DashboardReportDto> GetDashboardAsync(CancellationToken ct = default)
    {
        DateTime nowUtc = clock.UtcNow;
        DateOnly today = clock.LocalToday;
        string cacheKey = $"reporting:dashboard:{today:yyyy-MM-dd}";
        if (cache.TryGetValue(cacheKey, out DashboardReportDto? cached) && cached is not null)
            return cached;

        DateTime overdueCutoffUtc = clock.StartOfLocalDayUtc(today);
        DashboardCountsData counts = await repository.GetDashboardCountsAsync(overdueCutoffUtc, ct);
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

        cache.Set(cacheKey, report, TimeSpan.FromMinutes(options.Value.DashboardCacheMinutes));
        return report;
    }

    public async Task<TopBooksAndCategoriesReportDto> GetTopBooksAndCategoriesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (from == default || to == default || from > to)
	{
            throw new DomainException(ReportingErrors.InvalidDateRange);
	}

        DateTime fromUtc = clock.StartOfLocalDayUtc(from);
        DateTime toUtc = clock.EndOfLocalDayUtc(to);
        IReadOnlyList<TopBookReportItemDto> books = await repository.GetTopBooksAsync(fromUtc, toUtc, ct);
        IReadOnlyList<TopCategoryReportItemDto> categories = await repository.GetTopCategoriesAsync(fromUtc, toUtc, ct);

        return new TopBooksAndCategoriesReportDto(from, to, clock.UtcNow, books, categories);
    }


    public async Task<ReadingActivityByDepartmentReportDto> GetReadingActivityByDepartmentAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (from == default || to == default || from > to)
        {
            throw new DomainException(ReportingErrors.InvalidDateRange);
        }

        DateTime fromUtc = clock.StartOfLocalDayUtc(from);
        DateTime toUtc = clock.EndOfLocalDayUtc(to);
        IReadOnlyList<DepartmentEmployeesData> departments = await repository.GetDepartmentEmployeesAsync(ct);
        IReadOnlyList<DepartmentCategoryLoansData> loans = await repository.GetDepartmentCategoryLoansAsync(fromUtc, toUtc, ct);

        ILookup<Guid, DepartmentCategoryLoansData> loansByDepartment = loans.ToLookup(row => row.DepartmentId);

        // Every department is listed, also the ones without loans in the period.
        List<DepartmentReadingActivityDto> items = departments
            .Select(department =>
            {
                List<DepartmentCategoryLoansData> categories = loansByDepartment[department.DepartmentId].ToList();
                int loanCount = categories.Sum(row => row.LoanCount);
                DepartmentCategoryLoansData? top = categories
                    .OrderByDescending(row => row.LoanCount)
                    .ThenBy(row => row.Category)
                    .FirstOrDefault();
                decimal per10Employees = department.Employees == 0
                    ? 0m
                    : Math.Round(loanCount * 10m / department.Employees, 1, MidpointRounding.AwayFromZero);

                return new DepartmentReadingActivityDto(
                    department.DepartmentId,
                    department.Department,
                    department.Employees,
                    loanCount,
                    per10Employees,
                    top?.CategoryId,
                    top?.Category,
                    top?.LoanCount ?? 0);
            })
            .OrderBy(item => item.Department)
            .ToList();

        return new ReadingActivityByDepartmentReportDto(from, to, clock.UtcNow, items);
    }

    public async Task<InventoryStatusReportDto> GetInventoryStatusAsync(
        Guid? categoryId,
        CancellationToken ct = default)
    {
        if (categoryId == Guid.Empty)
        {
            throw new DomainException(ReportingErrors.InvalidCategoryId);
        }

        string? category = null;
        if (categoryId is Guid id)
        {
            category = await repository.GetCategoryNameAsync(id, ct)
                       ?? throw new DomainException(ReportingErrors.CategoryNotFound);
        }

        IReadOnlyList<CopyCountData> counts = await repository.GetCopyCountsAsync(categoryId, ct);
        Dictionary<(BookCopyStatus, BookCopyCondition), int> copies =
            counts.ToDictionary(row => (row.Status, row.Condition), row => row.Copies);

        // Every status and condition is listed, also the ones without copies.
        List<InventoryStatusItemDto> statuses = Enum.GetValues<BookCopyStatus>()
            .Select(status =>
            {
                List<InventoryConditionItemDto> conditions = Enum.GetValues<BookCopyCondition>()
                    .Select(condition => new InventoryConditionItemDto(
                        condition, copies.GetValueOrDefault((status, condition))))
                    .ToList();

                return new InventoryStatusItemDto(status, conditions.Sum(item => item.Copies), conditions);
            })
            .ToList();

        return new InventoryStatusReportDto(
            clock.LocalToday,
            clock.UtcNow,
            categoryId,
            category,
            statuses.Sum(item => item.Copies),
            statuses
                .Where(item => item.Status is not (BookCopyStatus.Lost or BookCopyStatus.Withdrawn))
                .Sum(item => item.Copies),
            statuses);
    }
}
