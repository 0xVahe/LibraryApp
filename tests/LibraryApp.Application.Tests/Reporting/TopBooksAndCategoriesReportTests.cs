
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

public sealed class TopBooksAndCategoriesReportTests
{
    private static readonly DateTime Now =
        new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeReportingRepository _repository = new();
    private readonly TestClock _clock = new(Now);

    private ReportingService CreateService() =>
        new(
            _repository,
            _clock,
            new UnusedCalendar(),
            new MemoryCache(new MemoryCacheOptions()),
            new PenaltyPolicy(new PenaltyRules(
    		PointsPerLateWorkingDay: 1,
    		MaxLatePoints: 10,
    		DamagedPoints: 5,
    		LostPoints: 10,
    		LifetimeDays: 90,
    		RestrictedFromPoints: 5,
    		SuspendedFromPoints: 10)));

    [Fact]
    public async Task Returns_books_and_categories_from_repository()
    {
        Guid bookId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();

        _repository.Books.Add(new TopBookReportItemDto(bookId, "Clean Code", 12));
        _repository.Categories.Add(new TopCategoryReportItemDto(categoryId, "Programming", 20));

        TopBooksAndCategoriesReportDto report =
            await CreateService().GetTopBooksAndCategoriesAsync(
                new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 16));

        Assert.Equal(new DateOnly(2026, 10, 1), report.PeriodStart);
        Assert.Equal(new DateOnly(2026, 10, 16), report.PeriodEnd);
        Assert.Equal(Now, report.GeneratedAtUtc);

        TopBookReportItemDto book = Assert.Single(report.TopBooks);
        Assert.Equal(bookId, book.BookId);
        Assert.Equal("Clean Code", book.Title);
        Assert.Equal(12, book.LoanCount);

        TopCategoryReportItemDto category = Assert.Single(report.TopCategories);
        Assert.Equal(categoryId, category.CategoryId);
        Assert.Equal("Programming", category.Name);
        Assert.Equal(20, category.LoanCount);
    }

    [Fact]
    public async Task Empty_repository_returns_empty_lists()
    {
        TopBooksAndCategoriesReportDto report =
            await CreateService().GetTopBooksAndCategoriesAsync(
                new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 16));

        Assert.Empty(report.TopBooks);
        Assert.Empty(report.TopCategories);
    }

    [Fact]
    public async Task Converts_local_date_range_to_utc_boundaries()
    {
        await CreateService().GetTopBooksAndCategoriesAsync(
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 16));

        Assert.Equal(
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            _repository.LastBooksFromUtc);

        Assert.Equal(
            new DateTime(2026, 10, 16, 23, 59, 59, DateTimeKind.Utc).AddTicks(9999999),
            _repository.LastBooksToUtc);

        Assert.Equal(_repository.LastBooksFromUtc, _repository.LastCategoriesFromUtc);
        Assert.Equal(_repository.LastBooksToUtc, _repository.LastCategoriesToUtc);
    }

    [Fact]
    public async Task Allows_single_day_date_range()
    {
        DateOnly date = new(2026, 10, 16);

        TopBooksAndCategoriesReportDto report =
            await CreateService().GetTopBooksAndCategoriesAsync(date, date);

        Assert.Equal(date, report.PeriodStart);
        Assert.Equal(date, report.PeriodEnd);
        Assert.Equal(1, _repository.BooksCallCount);
        Assert.Equal(1, _repository.CategoriesCallCount);
    }

    [Theory]
    [InlineData(2026, 10, 17, 2026, 10, 16)]
    [InlineData(2026, 12, 1, 2026, 1, 1)]
    public async Task Rejects_reversed_date_ranges(
        int fromYear, int fromMonth, int fromDay,
        int toYear, int toMonth, int toDay)
    {
        DomainException exception =
            await Assert.ThrowsAsync<DomainException>(() =>
                CreateService().GetTopBooksAndCategoriesAsync(
                    new DateOnly(fromYear, fromMonth, fromDay),
                    new DateOnly(toYear, toMonth, toDay)));

        Assert.Equal(ReportingErrors.InvalidDateRange, exception.Error);
        Assert.Equal(0, _repository.BooksCallCount);
        Assert.Equal(0, _repository.CategoriesCallCount);
    }

    [Fact]
    public async Task Rejects_default_start_date()
    {
        DomainException exception =
            await Assert.ThrowsAsync<DomainException>(() =>
                CreateService().GetTopBooksAndCategoriesAsync(
                    default,
                    new DateOnly(2026, 10, 16)));

        Assert.Equal(ReportingErrors.InvalidDateRange, exception.Error);
    }

    [Fact]
    public async Task Rejects_default_end_date()
    {
        DomainException exception =
            await Assert.ThrowsAsync<DomainException>(() =>
                CreateService().GetTopBooksAndCategoriesAsync(
                    new DateOnly(2026, 10, 1),
                    default));

        Assert.Equal(ReportingErrors.InvalidDateRange, exception.Error);
    }

    [Fact]
    public async Task Forwards_cancellation_token_to_both_queries()
    {
        using CancellationTokenSource cts = new();

        await CreateService().GetTopBooksAndCategoriesAsync(
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 16),
            cts.Token);

        Assert.Equal(cts.Token, _repository.BooksToken);
        Assert.Equal(cts.Token, _repository.CategoriesToken);
    }

    private sealed class FakeReportingRepository : IReportingReadRepository
    {
        public List<TopBookReportItemDto> Books { get; } = new();
        public List<TopCategoryReportItemDto> Categories { get; } = new();

        public DateTime? LastBooksFromUtc { get; private set; }
        public DateTime? LastBooksToUtc { get; private set; }
        public DateTime? LastCategoriesFromUtc { get; private set; }
        public DateTime? LastCategoriesToUtc { get; private set; }

        public CancellationToken BooksToken { get; private set; }
        public CancellationToken CategoriesToken { get; private set; }

        public int BooksCallCount { get; private set; }
        public int CategoriesCallCount { get; private set; }

        public Task<IReadOnlyList<TopBookReportItemDto>> GetTopBooksAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            LastBooksFromUtc = fromUtc;
            LastBooksToUtc = toUtc;
            BooksToken = ct;
            BooksCallCount++;

            return Task.FromResult<IReadOnlyList<TopBookReportItemDto>>(Books);
        }

        public Task<IReadOnlyList<TopCategoryReportItemDto>> GetTopCategoriesAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            LastCategoriesFromUtc = fromUtc;
            LastCategoriesToUtc = toUtc;
            CategoriesToken = ct;
            CategoriesCallCount++;

            return Task.FromResult<IReadOnlyList<TopCategoryReportItemDto>>(Categories);
        }

        public Task<IReadOnlyList<OverdueLoanData>> GetOverdueLoansAsync(
            DateTime nowUtc, Guid? departmentId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<DashboardCountsData> GetDashboardCountsAsync(
            DateTime nowUtc, CancellationToken ct = default) =>
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
