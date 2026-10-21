using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Options;
using LibraryApp.Application.Reporting.Ports;
using LibraryApp.Application.Reporting.Services;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Reporting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Tests.Reporting;

public sealed class ReadingActivityByDepartmentReportTests
{
    private static readonly DateTime Now =
        new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    private static readonly Guid FinanceId = Guid.NewGuid();
    private static readonly Guid ItId = Guid.NewGuid();
    private static readonly Guid RiskCategoryId = Guid.NewGuid();
    private static readonly Guid ItCategoryId = Guid.NewGuid();
    private static readonly Guid LeadershipCategoryId = Guid.NewGuid();

    private readonly FakeReportingRepository _repository = new();

    private ReportingService CreateService(IClock? clock = null) =>
        new(
            _repository,
            clock ?? new TestClock(Now),
            new UnusedCalendar(),
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
    public async Task Counts_loans_per_department_and_per_10_employees()
    {
        _repository.Departments.Add(new DepartmentEmployeesData(FinanceId, "Finance", 4));
        _repository.Loans.Add(new DepartmentCategoryLoansData(FinanceId, RiskCategoryId, "Risk, Compliance & AML", 3));
        _repository.Loans.Add(new DepartmentCategoryLoansData(FinanceId, LeadershipCategoryId, "Leadership & Management", 2));

        ReadingActivityByDepartmentReportDto report =
            await CreateService().GetReadingActivityByDepartmentAsync(From, To);

        DepartmentReadingActivityDto finance = Assert.Single(report.Departments);
        Assert.Equal(FinanceId, finance.DepartmentId);
        Assert.Equal("Finance", finance.Department);
        Assert.Equal(4, finance.Employees);
        Assert.Equal(5, finance.LoanCount);
        Assert.Equal(12.5m, finance.LoansPer10Employees);   // 5 loans * 10 / 4 employees
    }

    [Fact]
    public async Task Rounds_loans_per_10_employees_to_one_decimal()
    {
        _repository.Departments.Add(new DepartmentEmployeesData(FinanceId, "Finance", 3));
        _repository.Loans.Add(new DepartmentCategoryLoansData(FinanceId, RiskCategoryId, "Risk, Compliance & AML", 1));

        ReadingActivityByDepartmentReportDto report =
            await CreateService().GetReadingActivityByDepartmentAsync(From, To);

        Assert.Equal(3.3m, Assert.Single(report.Departments).LoansPer10Employees);   // 10 / 3 = 3.33
    }

    [Fact]
    public async Task Top_category_has_the_most_loans_and_a_tie_is_broken_by_name()
    {
        _repository.Departments.Add(new DepartmentEmployeesData(ItId, "IT", 5));
        _repository.Loans.Add(new DepartmentCategoryLoansData(ItId, RiskCategoryId, "Risk, Compliance & AML", 1));
        _repository.Loans.Add(new DepartmentCategoryLoansData(ItId, LeadershipCategoryId, "Leadership & Management", 3));
        _repository.Loans.Add(new DepartmentCategoryLoansData(ItId, ItCategoryId, "IT & Software Engineering", 3));

        ReadingActivityByDepartmentReportDto report =
            await CreateService().GetReadingActivityByDepartmentAsync(From, To);

        DepartmentReadingActivityDto it = Assert.Single(report.Departments);
        Assert.Equal(ItCategoryId, it.TopCategoryId);
        Assert.Equal("IT & Software Engineering", it.TopCategory);
        Assert.Equal(3, it.TopCategoryLoanCount);
    }

    [Fact]
    public async Task Departments_without_loans_or_without_employees_get_zeros()
    {
        _repository.Departments.Add(new DepartmentEmployeesData(FinanceId, "Finance", 4));
        _repository.Departments.Add(new DepartmentEmployeesData(ItId, "IT", 0));
        _repository.Loans.Add(new DepartmentCategoryLoansData(ItId, ItCategoryId, "IT & Software Engineering", 2));

        ReadingActivityByDepartmentReportDto report =
            await CreateService().GetReadingActivityByDepartmentAsync(From, To);

        DepartmentReadingActivityDto finance = report.Departments.Single(d => d.DepartmentId == FinanceId);
        Assert.Equal(0, finance.LoanCount);
        Assert.Equal(0m, finance.LoansPer10Employees);
        Assert.Null(finance.TopCategoryId);
        Assert.Null(finance.TopCategory);
        Assert.Equal(0, finance.TopCategoryLoanCount);

        DepartmentReadingActivityDto it = report.Departments.Single(d => d.DepartmentId == ItId);
        Assert.Equal(2, it.LoanCount);
        Assert.Equal(0m, it.LoansPer10Employees);   // no employees: no division by zero
    }

    [Fact]
    public async Task Lists_every_department_ordered_by_name()
    {
        _repository.Departments.Add(new DepartmentEmployeesData(ItId, "IT", 5));
        _repository.Departments.Add(new DepartmentEmployeesData(FinanceId, "Finance", 4));
        _repository.Loans.Add(new DepartmentCategoryLoansData(ItId, ItCategoryId, "IT & Software Engineering", 1));

        ReadingActivityByDepartmentReportDto report =
            await CreateService().GetReadingActivityByDepartmentAsync(From, To);

        Assert.Equal(["Finance", "IT"], report.Departments.Select(d => d.Department));
        Assert.Equal([0, 1], report.Departments.Select(d => d.LoanCount));
    }

    [Fact]
    public async Task States_the_period_and_the_generation_time()
    {
        ReadingActivityByDepartmentReportDto report =
            await CreateService().GetReadingActivityByDepartmentAsync(From, To);

        Assert.Equal(From, report.PeriodStart);
        Assert.Equal(To, report.PeriodEnd);
        Assert.Equal(Now, report.GeneratedAtUtc);
        Assert.Empty(report.Departments);
    }

    [Fact]
    public async Task Converts_the_local_date_range_to_utc_in_yerevan_time()
    {
        await CreateService(new YerevanClock(Now)).GetReadingActivityByDepartmentAsync(From, To);

        // 1 October 00:00 in Yerevan is 30 September 20:00 UTC.
        Assert.Equal(new DateTime(2026, 9, 30, 20, 0, 0, DateTimeKind.Utc), _repository.LastFromUtc);
        Assert.Equal(new DateTime(2026, 10, 31, 20, 0, 0, DateTimeKind.Utc).AddTicks(-1), _repository.LastToUtc);
    }

    [Fact]
    public async Task A_reversed_date_range_is_rejected()
    {
        DomainException ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateService().GetReadingActivityByDepartmentAsync(To, From));

        Assert.Equal(ReportingErrors.InvalidDateRange, ex.Error);
        Assert.Equal(0, _repository.CallCount);
    }

    private sealed class FakeReportingRepository : IReportingReadRepository
    {
        public List<DepartmentEmployeesData> Departments { get; } = new();
        public List<DepartmentCategoryLoansData> Loans { get; } = new();

        public DateTime? LastFromUtc { get; private set; }
        public DateTime? LastToUtc { get; private set; }
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<DepartmentEmployeesData>> GetDepartmentEmployeesAsync(
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<DepartmentEmployeesData>>(Departments);
        }

        public Task<IReadOnlyList<DepartmentCategoryLoansData>> GetDepartmentCategoryLoansAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            LastFromUtc = fromUtc;
            LastToUtc = toUtc;
            CallCount++;
            return Task.FromResult<IReadOnlyList<DepartmentCategoryLoansData>>(Loans);
        }

        public Task<IReadOnlyList<OverdueLoanData>> GetOverdueLoansAsync(
            DateTime nowUtc, Guid? departmentId, CancellationToken ct = default) =>
            throw new NotSupportedException();

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

    /// <summary>Asia/Yerevan is UTC+4 all year (no daylight saving).</summary>
    private sealed class YerevanClock(DateTime utcNow) : IClock
    {
        private static readonly TimeSpan Offset = TimeSpan.FromHours(4);

        public DateTime UtcNow => utcNow;

        public DateOnly LocalToday => ToLocalDate(UtcNow);

        public DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);

        public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(utc + Offset);

        public DateTime StartOfLocalDayUtc(DateOnly localDate) =>
            localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - Offset;

        public DateTime EndOfLocalDayUtc(DateOnly localDate) => StartOfLocalDayUtc(localDate.AddDays(1)).AddTicks(-1);
    }

    private sealed class UnusedCalendar : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) =>
            throw new NotSupportedException();

        public DateOnly AddWorkingDays(DateOnly from, int days) =>
            throw new NotSupportedException();

        public int CountWorkingDays(
            DateOnly fromExclusive, DateOnly toInclusive) =>
            throw new NotSupportedException();
    }
}