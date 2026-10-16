
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
            new PenaltyPolicy());

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
        Assert.Equal(PenaltyPolicy.MaxLatePoints, item.PointsSoFar);
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
