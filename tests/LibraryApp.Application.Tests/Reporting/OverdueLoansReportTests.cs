using LibraryApp.Application.Reporting.Options;
using Microsoft.Extensions.Options;
using LibraryApp.Application.Common;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Ports;
using LibraryApp.Application.Reporting.Services;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Reporting;
using Microsoft.Extensions.Caching.Memory;

namespace LibraryApp.Application.Tests.Reporting;

public sealed class OverdueLoansReportTests
{
    private static readonly DateTime Now =
        new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeReportingRepository _repository = new();
    private readonly TestClock _clock = new(Now);
    private readonly WeekdayCalendar _calendar = new();

    private ReportingService CreateService() =>
        new(
            _repository,
            _clock,
            _calendar,
            new MemoryCache(new MemoryCacheOptions()),
            new PenaltyPolicy(new PenaltyRules(
            PointsPerLateWorkingDay: 1,
            MaxLatePoints: 10,
            DamagedPoints: 5,
            LostPoints: 10,
            LifetimeDays: 90,
            RestrictedFromPoints: 5,
            SuspendedFromPoints: 10)),
        Options.Create(new ReportingOptions
        {
            DashboardCacheMinutes = 5,
            TopBooksLimit = 10,
            TopCategoriesLimit = 5
        }));

    [Fact]
    public async Task Empty_report_returns_no_items()
    {
        OverdueLoansReportDto report =
            await CreateService().GetOverdueLoansAsync(null, null, null);

        Assert.Empty(report.Items);
        Assert.Equal(new DateOnly(2026, 10, 16), report.AsOfDate);
        Assert.Equal(Now, report.GeneratedAtUtc);
    }

    [Fact]
    public async Task Uses_start_of_local_day_as_cutoff()
    {
        await CreateService().GetOverdueLoansAsync(null, null, null);

        Assert.Equal(
            new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc),
            _repository.LastOverdueCutoffUtc);
    }

    [Fact]
    public async Task Calculates_working_days_excluding_weekends()
    {
        AddLoan(new DateOnly(2026, 10, 9));

        OverdueLoansReportDto report =
            await CreateService().GetOverdueLoansAsync(null, null, null);

        OverdueLoanItemDto item = Assert.Single(report.Items);

        Assert.Equal(5, item.WorkingDaysOverdue);
        Assert.Equal(5, item.PointsSoFar);
    }

    [Fact]
    public async Task Penalty_points_are_capped_at_ten()
    {
        AddLoan(new DateOnly(2026, 9, 1));

        OverdueLoansReportDto report =
            await CreateService().GetOverdueLoansAsync(null, null, null);

        OverdueLoanItemDto item = Assert.Single(report.Items);

        Assert.True(item.WorkingDaysOverdue > 10);
        Assert.Equal(10, item.PointsSoFar);
    }

    [Fact]
    public async Task Filters_by_department()
    {
        Guid requestedDepartment = Guid.NewGuid();

        AddLoan(new DateOnly(2026, 10, 12), requestedDepartment);
        AddLoan(new DateOnly(2026, 10, 13), Guid.NewGuid());

        OverdueLoansReportDto report =
            await CreateService().GetOverdueLoansAsync(requestedDepartment, null, null);

        Assert.Single(report.Items);
        Assert.Equal(requestedDepartment, report.DepartmentId);
    }

    [Fact]
    public async Task Filters_by_minimum_and_maximum_working_days()
    {
        AddLoan(new DateOnly(2026, 10, 14));
        AddLoan(new DateOnly(2026, 10, 12));
        AddLoan(new DateOnly(2026, 10, 9));

        OverdueLoansReportDto report =
            await CreateService().GetOverdueLoansAsync(null, 3, 4);

        OverdueLoanItemDto item = Assert.Single(report.Items);

        Assert.Equal(4, item.WorkingDaysOverdue);
    }

    [Fact]
    public async Task Orders_by_overdue_days_descending()
    {
        AddLoan(new DateOnly(2026, 10, 14));
        AddLoan(new DateOnly(2026, 10, 9));
        AddLoan(new DateOnly(2026, 10, 12));

        OverdueLoansReportDto report =
            await CreateService().GetOverdueLoansAsync(null, null, null);

        Assert.Equal(
            new[] { 5, 4, 2 },
            report.Items.Select(item => item.WorkingDaysOverdue));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, -1)]
    [InlineData(5, 2)]
    public async Task Invalid_overdue_filters_throw_domain_exception(
        int? minimum, int? maximum)
    {
        DomainException exception =
            await Assert.ThrowsAsync<DomainException>(() =>
                CreateService().GetOverdueLoansAsync(null, minimum, maximum));

        Assert.Equal(ReportingErrors.InvalidOverdueFilter, exception.Error);
    }

    [Fact]
    public async Task Empty_department_id_throws_domain_exception()
    {
        DomainException exception =
            await Assert.ThrowsAsync<DomainException>(() =>
                CreateService().GetOverdueLoansAsync(Guid.Empty, null, null));

        Assert.Equal(ReportingErrors.InvalidDepartmentId, exception.Error);
    }


    [Fact]
    public async Task Default_pagination_returns_first_twenty_items()
    {
        for (int i = 0; i < 25; i++)
            AddLoan(new DateOnly(2026, 10, 9));

        OverdueLoansReportDto report =
            await CreateService().GetOverdueLoansAsync(null, null, null);

        Assert.Equal(20, report.Items.Count);
        Assert.Equal(25, report.TotalCount);
        Assert.Equal(1, report.Page);
        Assert.Equal(20, report.PageSize);
        Assert.Equal(2, report.TotalPages);
    }

    [Fact]
    public async Task Custom_pagination_returns_requested_page()
    {
        for (int i = 0; i < 12; i++)
            AddLoan(new DateOnly(2026, 10, 9));

        OverdueLoansReportDto report =
            await CreateService().GetOverdueLoansAsync(
                null, null, null, new PageRequest(2, 5));

        Assert.Equal(5, report.Items.Count);
        Assert.Equal(12, report.TotalCount);
        Assert.Equal(2, report.Page);
        Assert.Equal(5, report.PageSize);
        Assert.Equal(3, report.TotalPages);
    }

    [Fact]
    public async Task Page_beyond_available_results_returns_empty_items()
    {
        for (int i = 0; i < 3; i++)
            AddLoan(new DateOnly(2026, 10, 9));

        OverdueLoansReportDto report =
            await CreateService().GetOverdueLoansAsync(
                null, null, null, new PageRequest(5, 2));

        Assert.Empty(report.Items);
        Assert.Equal(3, report.TotalCount);
        Assert.Equal(5, report.Page);
        Assert.Equal(2, report.TotalPages);
    }

    [Fact]
    public async Task Filters_before_paginating()
    {
        AddLoan(new DateOnly(2026, 10, 14));
        AddLoan(new DateOnly(2026, 10, 13));
        AddLoan(new DateOnly(2026, 10, 12));
        AddLoan(new DateOnly(2026, 10, 9));

        OverdueLoansReportDto report =
            await CreateService().GetOverdueLoansAsync(
                null, 3, null, new PageRequest(1, 2));

        Assert.Equal(2, report.Items.Count);
        Assert.Equal(3, report.TotalCount);
        Assert.Equal(2, report.TotalPages);
        Assert.Equal(new[] { 5, 4 },
            report.Items.Select(item => item.WorkingDaysOverdue));
    }

    [Fact]
    public async Task Page_request_normalizes_invalid_sizes()
    {
        for (int i = 0; i < 25; i++)
            AddLoan(new DateOnly(2026, 10, 9));

        OverdueLoansReportDto report =
            await CreateService().GetOverdueLoansAsync(
                null, null, null, new PageRequest(0, 500));

        Assert.Equal(1, report.Page);
        Assert.Equal(PageRequest.MaxPageSize, report.PageSize);
        Assert.Equal(25, report.Items.Count);
        Assert.Equal(25, report.TotalCount);
        Assert.Equal(1, report.TotalPages);
    }


    private void AddLoan(DateOnly dueDate, Guid? departmentId = null)
    {
        _repository.Loans.Add(new OverdueLoanData(
            Guid.NewGuid(),
            "EMP-001",
            "Test Employee",
            "Engineering",
            departmentId ?? Guid.NewGuid(),
            null,
            "Test Book",
            dueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            0));
    }

    private sealed class FakeReportingRepository : IReportingReadRepository
    {
        public List<OverdueLoanData> Loans { get; } = new();

        public DateTime? LastOverdueCutoffUtc { get; private set; }

        public Task<IReadOnlyList<OverdueLoanData>> GetOverdueLoansAsync(
            DateTime nowUtc, Guid? departmentId, CancellationToken ct = default)
        {
            LastOverdueCutoffUtc = nowUtc;
            return Task.FromResult<IReadOnlyList<OverdueLoanData>>(

        Loans
            .Where(loan => !departmentId.HasValue || loan.DepartmentId == departmentId.Value)
            .ToList());
        }

        public Task<DashboardCountsData> GetDashboardCountsAsync(
            DateTime nowUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TopBookReportItemDto>> GetTopBooksAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TopCategoryReportItemDto>> GetTopCategoriesAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class WeekdayCalendar : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) =>
            date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        public DateOnly AddWorkingDays(DateOnly from, int days)
        {
            DateOnly date = from;

            while (days > 0)
            {
                date = date.AddDays(1);

                if (IsWorkingDay(date))
                    days--;
            }

            return date;
        }

        public int CountWorkingDays(
            DateOnly fromExclusive, DateOnly toInclusive)
        {
            int count = 0;

            for (DateOnly date = fromExclusive.AddDays(1);
                 date <= toInclusive;
                 date = date.AddDays(1))
            {
                if (IsWorkingDay(date))
                    count++;
            }

            return count;
        }
    }
}
