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

public sealed class PenaltyReportTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    private static readonly Guid FinanceId = Guid.NewGuid();
    private static readonly Guid ItId = Guid.NewGuid();

    private readonly FakeReportingRepository _repository = new();

    private ReportingService CreateService() =>
        new(
            _repository,
            new TestClock(Now),
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
    public async Task Shows_the_points_of_each_department()
    {
        _repository.Departments.Add(new DepartmentEmployeesData(FinanceId, "Finance", 4));
        _repository.Penalties.Add(new DepartmentPenaltiesData(FinanceId, 4, 10, 2, 3));

        PenaltyReportDto report = await CreateService().GetPenaltiesByDepartmentAsync(From, To);

        Assert.Equal(new DepartmentPenaltiesDto(FinanceId, "Finance", 4, 10, 2, 3), Assert.Single(report.Departments));
    }

    [Fact]
    public async Task Lists_every_department_by_name_with_zeros_when_it_has_no_penalties()
    {
        _repository.Departments.Add(new DepartmentEmployeesData(ItId, "IT", 5));
        _repository.Departments.Add(new DepartmentEmployeesData(FinanceId, "Finance", 4));
        _repository.Penalties.Add(new DepartmentPenaltiesData(ItId, 1, 5, 0, 5));

        PenaltyReportDto report = await CreateService().GetPenaltiesByDepartmentAsync(From, To);

        Assert.Equal(["Finance", "IT"], report.Departments.Select(d => d.Department));
        Assert.Equal(new DepartmentPenaltiesDto(FinanceId, "Finance", 0, 0, 0, 0), report.Departments[0]);
        Assert.Equal(5, report.Departments[1].IssuedPoints);
    }

    [Fact]
    public async Task Passes_the_period_and_the_current_time_to_the_query()
    {
        PenaltyReportDto report = await CreateService().GetPenaltiesByDepartmentAsync(From, To);

        Assert.Equal(From, report.PeriodStart);
        Assert.Equal(To, report.PeriodEnd);
        Assert.Equal(Now, report.GeneratedAtUtc);
        Assert.Empty(report.Departments);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), _repository.LastFromUtc);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1), _repository.LastToUtc);
        Assert.Equal(Now, _repository.LastNowUtc);
    }

    [Fact]
    public async Task A_reversed_date_range_is_rejected()
    {
        DomainException ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateService().GetPenaltiesByDepartmentAsync(To, From));

        Assert.Equal(ReportingErrors.InvalidDateRange, ex.Error);
        Assert.Equal(0, _repository.CallCount);
    }

    private sealed class FakeReportingRepository : IReportingReadRepository
    {
        public List<DepartmentEmployeesData> Departments { get; } = new();
        public List<DepartmentPenaltiesData> Penalties { get; } = new();

        public DateTime? LastFromUtc { get; private set; }
        public DateTime? LastToUtc { get; private set; }
        public DateTime? LastNowUtc { get; private set; }
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<DepartmentEmployeesData>> GetDepartmentEmployeesAsync(
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<DepartmentEmployeesData>>(Departments);
        }

        public Task<IReadOnlyList<DepartmentPenaltiesData>> GetDepartmentPenaltiesAsync(
            DateTime fromUtc, DateTime toUtc, DateTime nowUtc, CancellationToken ct = default)
        {
            LastFromUtc = fromUtc;
            LastToUtc = toUtc;
            LastNowUtc = nowUtc;
            CallCount++;
            return Task.FromResult<IReadOnlyList<DepartmentPenaltiesData>>(Penalties);
        }

        public Task<IReadOnlyList<DepartmentCategoryLoansData>> GetDepartmentCategoryLoansAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();

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

    private sealed class UnusedCalendar : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) => throw new NotSupportedException();

        public DateOnly AddWorkingDays(DateOnly from, int days) => throw new NotSupportedException();

        public int CountWorkingDays(DateOnly fromExclusive, DateOnly toInclusive) => throw new NotSupportedException();
    }
}
