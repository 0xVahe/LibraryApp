using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Options;
using LibraryApp.Application.Reporting.Ports;
using LibraryApp.Application.Reporting.Services;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Borrowing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Tests.Reporting;

public sealed class PurchaseCandidatesReportTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

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
                TopCategoriesLimit = 5,
                PurchaseCandidatesLimit = 10
            }));

    [Fact]
    public async Task Lists_the_books_from_the_repository_in_its_order_with_the_generation_time()
    {
        _repository.Candidates.Add(new PurchaseCandidateItemDto(
            Guid.NewGuid(), "Clean Code", "9780132350884", "Technology", 4, 1));
        _repository.Candidates.Add(new PurchaseCandidateItemDto(
            Guid.NewGuid(), "Effective Java", "9780134685991", "Technology", 2, 3));

        PurchaseCandidatesReportDto report = await CreateService().GetPurchaseCandidatesAsync();

        Assert.Equal(Now, report.GeneratedAtUtc);
        Assert.Equal(["Clean Code", "Effective Java"], report.Books.Select(book => book.Title));
        Assert.Equal(4, report.Books[0].WaitingReservations);
        Assert.Equal(1, report.Books[0].Copies);
    }

    [Fact]
    public async Task Without_waiting_reservations_the_list_is_empty()
    {
        PurchaseCandidatesReportDto report = await CreateService().GetPurchaseCandidatesAsync();

        Assert.Empty(report.Books);
    }

    private sealed class FakeReportingRepository : IReportingReadRepository
    {
        public List<PurchaseCandidateItemDto> Candidates { get; } = new();

        public Task<IReadOnlyList<PurchaseCandidateItemDto>> GetPurchaseCandidatesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PurchaseCandidateItemDto>>(Candidates);

        public Task<string?> GetCategoryNameAsync(Guid categoryId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CopyCountData>> GetCopyCountsAsync(Guid? categoryId, CancellationToken ct = default) =>
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
