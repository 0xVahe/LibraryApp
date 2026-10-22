using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Options;
using LibraryApp.Application.Reporting.Ports;
using LibraryApp.Application.Reporting.Services;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Reporting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Tests.Reporting;

public sealed class InventoryStatusReportTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TechnologyId = Guid.NewGuid();

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
    public async Task Counts_copies_per_status_and_condition()
    {
        _repository.Counts.Add(new CopyCountData(BookCopyStatus.Available, BookCopyCondition.Good, 3));
        _repository.Counts.Add(new CopyCountData(BookCopyStatus.Available, BookCopyCondition.Poor, 1));
        _repository.Counts.Add(new CopyCountData(BookCopyStatus.OnLoan, BookCopyCondition.New, 2));

        InventoryStatusReportDto report = await CreateService().GetInventoryStatusAsync(null);

        Assert.Equal(6, report.TotalCopies);
        InventoryStatusItemDto available = report.Statuses.Single(item => item.Status == BookCopyStatus.Available);
        Assert.Equal(4, available.Copies);
        Assert.Equal(3, available.Conditions.Single(item => item.Condition == BookCopyCondition.Good).Copies);
        Assert.Equal(1, available.Conditions.Single(item => item.Condition == BookCopyCondition.Poor).Copies);
        Assert.Equal(2, report.Statuses.Single(item => item.Status == BookCopyStatus.OnLoan).Copies);
    }

    [Fact]
    public async Task Every_status_and_condition_is_listed_also_without_copies()
    {
        InventoryStatusReportDto report = await CreateService().GetInventoryStatusAsync(null);

        Assert.Equal(0, report.TotalCopies);
        Assert.Equal(Enum.GetValues<BookCopyStatus>(), report.Statuses.Select(item => item.Status));
        Assert.All(report.Statuses, item =>
        {
            Assert.Equal(0, item.Copies);
            Assert.Equal(Enum.GetValues<BookCopyCondition>(), item.Conditions.Select(condition => condition.Condition));
            Assert.All(item.Conditions, condition => Assert.Equal(0, condition.Copies));
        });
    }

    [Fact]
    public async Task The_whole_library_without_a_category()
    {
        InventoryStatusReportDto report = await CreateService().GetInventoryStatusAsync(null);

        Assert.Null(report.CategoryId);
        Assert.Null(report.Category);
        Assert.Null(_repository.LastCategoryId);
        Assert.Equal(Now, report.GeneratedAtUtc);
    }

    [Fact]
    public async Task A_category_filters_the_copies_and_is_named_in_the_report()
    {
        _repository.Categories[TechnologyId] = "Technology";

        InventoryStatusReportDto report = await CreateService().GetInventoryStatusAsync(TechnologyId);

        Assert.Equal(TechnologyId, report.CategoryId);
        Assert.Equal("Technology", report.Category);
        Assert.Equal(TechnologyId, _repository.LastCategoryId);
    }

    [Fact]
    public async Task An_unknown_category_is_not_found()
    {
        DomainException ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateService().GetInventoryStatusAsync(Guid.NewGuid()));

        Assert.Equal(ReportingErrors.CategoryNotFound, ex.Error);
        Assert.Null(_repository.LastCategoryId);
    }

    [Fact]
    public async Task An_empty_category_id_is_rejected()
    {
        DomainException ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateService().GetInventoryStatusAsync(Guid.Empty));

        Assert.Equal(ReportingErrors.InvalidCategoryId, ex.Error);
    }

    private sealed class FakeReportingRepository : IReportingReadRepository
    {
        public List<CopyCountData> Counts { get; } = new();
        public Dictionary<Guid, string> Categories { get; } = new();
        public Guid? LastCategoryId { get; private set; }

        public Task<string?> GetCategoryNameAsync(Guid categoryId, CancellationToken ct = default) =>
            Task.FromResult(Categories.GetValueOrDefault(categoryId));

        public Task<IReadOnlyList<CopyCountData>> GetCopyCountsAsync(Guid? categoryId, CancellationToken ct = default)
        {
            LastCategoryId = categoryId;
            return Task.FromResult<IReadOnlyList<CopyCountData>>(Counts);
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

        public Task<IReadOnlyList<DepartmentEmployeesData>> GetDepartmentEmployeesAsync(
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DepartmentCategoryLoansData>> GetDepartmentCategoryLoansAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();
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
