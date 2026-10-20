using LibraryApp.Application.Reporting.Options;
using Microsoft.Extensions.Options;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Ports;
using LibraryApp.Application.Reporting.Services;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Borrowing;
using Microsoft.Extensions.Caching.Memory;

namespace LibraryApp.Application.Tests.Reporting;

public sealed class DashboardReportTests
{
    private static readonly DateTime Now =
        new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeReportingRepository _repository = new();
    private readonly TestClock _clock = new(Now);
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private ReportingService CreateService() =>
        new(
            _repository,
            _clock,
            new UnusedCalendar(),
            _cache,
            new PenaltyPolicy(new PenaltyRules(
            PointsPerLateWorkingDay: 1,
            MaxLatePoints: 10,
            DamagedPoints: 5,
            LostPoints: 10,
            LifetimeDays: 90,
            RestrictedFromPoints: 5,
            SuspendedFromPoints: 10)),
        Options.Create(new ReportingOptions()));

    [Fact]
    public async Task Uses_configured_dashboard_cache_expiration()
    {
        var timeProvider = new AdjustableTimeProvider();

        using var cache = new MemoryCache(new MemoryCacheOptions
        {
            Clock = new TimeProviderClock(timeProvider),
            ExpirationScanFrequency = TimeSpan.Zero
        });

        var options = Options.Create(new ReportingOptions
        {
            DashboardCacheMinutes = 2
        });

        var service = new ReportingService(
            _repository,
            _clock,
            new UnusedCalendar(),
            cache,
            new PenaltyPolicy(new PenaltyRules(
                PointsPerLateWorkingDay: 1,
                MaxLatePoints: 10,
                DamagedPoints: 5,
                LostPoints: 10,
                LifetimeDays: 90,
                RestrictedFromPoints: 5,
                SuspendedFromPoints: 10)),
            options);

        DashboardReportDto first = await service.GetDashboardAsync();

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        DashboardReportDto beforeExpiration = await service.GetDashboardAsync();

        Assert.Same(first, beforeExpiration);

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        DashboardReportDto afterExpiration = await service.GetDashboardAsync();

        Assert.NotSame(first, afterExpiration);
    }

    private sealed class TimeProviderClock : Microsoft.Extensions.Internal.ISystemClock
    {
        private readonly TimeProvider _timeProvider;

        public TimeProviderClock(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => _now.UtcTicks;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    [Fact]
    public async Task Returns_dashboard_counts()
    {
        _repository.Counts = new DashboardCountsData(
            100, 250, 80, 40, 200, 20, 5, 2);

        DashboardReportDto report = await CreateService().GetDashboardAsync();

        Assert.Equal(new DateOnly(2026, 10, 16), report.AsOfDate);
        Assert.Equal(Now, report.GeneratedAtUtc);
        Assert.Equal(100, report.TotalBooks);
        Assert.Equal(250, report.TotalCopies);
        Assert.Equal(80, report.ActiveReaders);
        Assert.Equal(40, report.ActiveLoans);
        Assert.Equal(20, report.OverdueLoans);
        Assert.Equal(10m, report.OverduePercentage);
        Assert.Equal(5, report.RestrictedEmployees);
        Assert.Equal(2, report.SuspendedEmployees);
    }

    [Fact]
    public async Task Uses_start_of_local_day_as_overdue_cutoff()
    {
        await CreateService().GetDashboardAsync();

        Assert.Equal(
            new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc),
            _repository.LastCutoffUtc);
    }

    [Fact]
    public async Task Returns_zero_percentage_when_there_are_no_loans()
    {
        _repository.Counts = new DashboardCountsData(
            10, 20, 0, 0, 0, 0, 0, 0);

        DashboardReportDto report = await CreateService().GetDashboardAsync();

        Assert.Equal(0m, report.OverduePercentage);
    }

    [Theory]
    [InlineData(1, 3, "33.3")]
    [InlineData(2, 3, "66.7")]
    [InlineData(1, 16, "6.3")]
    [InlineData(1, 8, "12.5")]
    public async Task Calculates_and_rounds_overdue_percentage(
        int overdue, int total, string expected)
    {
        _repository.Counts = new DashboardCountsData(
            10, 20, 5, 3, total, overdue, 0, 0);

        DashboardReportDto report = await CreateService().GetDashboardAsync();

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            report.OverduePercentage);
    }

    [Fact]
    public async Task Reuses_cached_dashboard_on_same_day()
    {
        ReportingService service = CreateService();

        DashboardReportDto first = await service.GetDashboardAsync();

        _repository.Counts = new DashboardCountsData(
            999, 999, 999, 999, 999, 999, 999, 999);

        DashboardReportDto second = await service.GetDashboardAsync();

        Assert.Same(first, second);
        Assert.Equal(1, _repository.DashboardCallCount);
    }

    [Fact]
    public async Task Cache_is_shared_between_service_instances()
    {
        DashboardReportDto first = await CreateService().GetDashboardAsync();
        DashboardReportDto second = await CreateService().GetDashboardAsync();

        Assert.Same(first, second);
        Assert.Equal(1, _repository.DashboardCallCount);
    }

    [Fact]
    public async Task New_day_uses_new_cache_key()
    {
        ReportingService service = CreateService();

        await service.GetDashboardAsync();

        _clock.UtcNow = new DateTime(
            2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

        DashboardReportDto report = await service.GetDashboardAsync();

        Assert.Equal(new DateOnly(2026, 10, 17), report.AsOfDate);
        Assert.Equal(2, _repository.DashboardCallCount);
    }

    private sealed class FakeReportingRepository : IReportingReadRepository
    {
        public DashboardCountsData Counts { get; set; } =
            new(0, 0, 0, 0, 0, 0, 0, 0);

        public DateTime? LastCutoffUtc { get; private set; }

        public int DashboardCallCount { get; private set; }

        public Task<DashboardCountsData> GetDashboardCountsAsync(
            DateTime nowUtc, CancellationToken ct = default)
        {
            LastCutoffUtc = nowUtc;
            DashboardCallCount++;
            return Task.FromResult(Counts);
        }

        public Task<IReadOnlyList<OverdueLoanData>> GetOverdueLoansAsync(
            DateTime nowUtc, Guid? departmentId, CancellationToken ct = default) =>
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
        public bool IsWorkingDay(DateOnly date) =>
            throw new NotSupportedException();

        public DateOnly AddWorkingDays(DateOnly from, int days) =>
            throw new NotSupportedException();

        public int CountWorkingDays(
            DateOnly fromExclusive, DateOnly toInclusive) =>
            throw new NotSupportedException();
    }
}
