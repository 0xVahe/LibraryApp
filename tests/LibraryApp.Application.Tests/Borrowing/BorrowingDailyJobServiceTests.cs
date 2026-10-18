using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Tests.Borrowing;

public sealed class BorrowingDailyJobServiceTests
{
    // 20 October at 12:00 in Yerevan.
    private static readonly DateTime DueUtc =
        new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private readonly Guid _staffId = Guid.NewGuid();
    private readonly Guid _bookId = Guid.NewGuid();
    private readonly Guid _copyId = Guid.NewGuid();

    private readonly FakeLoanRepository _loans = new();
    private readonly InMemoryCatalogService _catalog = new();
    private readonly FakeEventBus _events = new();
    private readonly FakeApplicationLock _locks = new();
    private readonly FixedYerevanClock _clock = new(DueUtc);

    private readonly FakeUnitOfWork _unitOfWork;

    public BorrowingDailyJobServiceTests()
    {
        _unitOfWork = new FakeUnitOfWork(
            _loans,
            _catalog,
            _events);
    }

    private BorrowingDailyJobService Service(
        OverdueFromMode mode = OverdueFromMode.NextLocalDay)
    {
        var options = BorrowingTestSettings.Create(
            settings => settings.Loans.OverdueFrom = mode);

        return new BorrowingDailyJobService(
            _loans,
            _catalog,
            new UnusedPenaltyService(),
            new OverdueRule(_clock, options),
            _unitOfWork,
            _locks,
            _events,
            _clock,
            new FakeIdGenerator());
    }

    private Loan SeedLoan(
        bool includeCopy = true,
        DateTime? dueAtUtc = null)
    {
        DateTime due = dueAtUtc ?? DueUtc;

        Loan loan = Loan.Issue(
            Guid.NewGuid(),
            _copyId,
            _staffId,
            due.AddDays(-1),
            loanDays: 1);

        _loans.Seed(loan);

        if (includeCopy)
        {
            _catalog.Seed(new BookCopyDto(
                _copyId,
                _bookId,
                "DAILY-001",
                BookCopyStatus.OnLoan,
                BookCopyCondition.Good,
                "A-1"));
        }

        return loan;
    }

    [Theory]
    [InlineData(19, 59, 59, false)]
    [InlineData(20, 0, 0, true)]
    public async Task NextLocalDay_uses_Yerevan_midnight(
        int utcHour,
        int utcMinute,
        int utcSecond,
        bool expectedChange)
    {
        Loan loan = SeedLoan();

        // 19:59:59 UTC = 23:59:59 on the local due date.
        // 20:00:00 UTC = 00:00:00 on the next local date.
        _clock.UtcNow = new DateTime(
            2026, 10, 20,
            utcHour, utcMinute, utcSecond,
            DateTimeKind.Utc);

        bool changed = await Service().MarkLoanOverdueAsync(loan.Id);

        Assert.Equal(expectedChange, changed);

        Assert.Equal(
            expectedChange ? LoanStatus.Overdue : LoanStatus.Active,
            loan.Status);

        Assert.Equal(
            expectedChange ? 1 : 0,
            _events.Published.OfType<LoanOverdue>().Count());
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task DueTime_requires_the_exact_deadline_to_have_passed(
        int ticksAfterDue,
        bool expectedChange)
    {
        Loan loan = SeedLoan();
        _clock.UtcNow = DueUtc.AddTicks(ticksAfterDue);

        bool changed = await Service(OverdueFromMode.DueTime)
            .MarkLoanOverdueAsync(loan.Id);

        Assert.Equal(expectedChange, changed);

        Assert.Equal(
            expectedChange ? LoanStatus.Overdue : LoanStatus.Active,
            loan.Status);
    }

    [Fact]
    public async Task Repeated_processing_publishes_one_correct_event()
    {
        Loan loan = SeedLoan();
        _clock.UtcNow = DueUtc.AddDays(1);

        BorrowingDailyJobService service = Service();

        Assert.True(await service.MarkLoanOverdueAsync(loan.Id));
        Assert.False(await service.MarkLoanOverdueAsync(loan.Id));

        var overdue = Assert.Single(
            _events.Published.OfType<LoanOverdue>());

        Assert.Equal(loan.Id, overdue.LoanId);
        Assert.Equal(_staffId, overdue.StaffMemberId);
        Assert.Equal(_bookId, overdue.BookId);
        Assert.Equal(DueUtc, overdue.DueAtUtc);
        Assert.Equal(_clock.UtcNow, overdue.OccurredAtUtc);
        Assert.NotEqual(Guid.Empty, overdue.EventId);

        Assert.Equal(LoanStatus.Overdue, loan.Status);

        // Becoming overdue does not change the physical copy's status.
        Assert.Equal(0, _catalog.SetStatusCalls);

        // Each call owns its own transaction, including the no-op call.
        Assert.Equal(2, _unitOfWork.CommittedCount);
    }

    [Fact]
    public async Task Locks_are_acquired_staff_first_then_book()
    {
        Loan loan = SeedLoan();
        _clock.UtcNow = DueUtc.AddDays(1);

        await Service().MarkLoanOverdueAsync(loan.Id);

        Assert.Equal(
            new[]
            {
                $"lock:{BorrowingLocks.Staff(_staffId)}",
                $"lock:{BorrowingLocks.Book(_bookId)}"
            },
            _locks.Log);
    }

    [Theory]
    [InlineData(LoanStatus.Returned)]
    [InlineData(LoanStatus.Lost)]
    [InlineData(LoanStatus.Overdue)]
    public async Task A_loan_that_is_no_longer_Active_is_skipped(
        LoanStatus status)
    {
        Loan loan = SeedLoan();
        _clock.UtcNow = DueUtc.AddDays(1);

        switch (status)
        {
            case LoanStatus.Returned:
                loan.Return(DueUtc);
                break;

            case LoanStatus.Lost:
                loan.MarkLost(DueUtc);
                break;

            case LoanStatus.Overdue:
                loan.MarkOverdue(_clock.UtcNow);
                break;
        }

        bool changed = await Service().MarkLoanOverdueAsync(loan.Id);

        Assert.False(changed);
        Assert.Equal(status, loan.Status);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task A_renewed_loan_is_checked_again_and_skipped()
    {
        Loan loan = SeedLoan();
        _clock.UtcNow = DueUtc.AddDays(1);

        // Represents a renewal after candidate selection,
        // but before the worker processes this loan.
        loan.Renew(7);

        bool changed = await Service().MarkLoanOverdueAsync(loan.Id);

        Assert.False(changed);
        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task An_unknown_loan_is_skipped()
    {
        bool changed = await Service()
            .MarkLoanOverdueAsync(Guid.NewGuid());

        Assert.False(changed);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task A_missing_copy_is_reported_without_marking_the_loan()
    {
        Loan loan = SeedLoan(includeCopy: false);
        _clock.UtcNow = DueUtc.AddDays(1);

        DomainException exception =
            await Assert.ThrowsAsync<DomainException>(
                () => Service().MarkLoanOverdueAsync(loan.Id));

        Assert.Equal(
            BorrowingErrors.CopyNotFound.Code,
            exception.Error.Code);

        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Empty(_events.Published);
        Assert.Equal(1, _unitOfWork.RolledBackCount);
    }

    [Theory]
    [InlineData(2026, 10, 18)] // Sunday.
    [InlineData(2026, 1, 1)]   // Holiday in the supplied configuration.
    public async Task Overdue_marking_does_not_wait_for_a_working_day(
        int year,
        int month,
        int day)
    {
        var localRunDate = new DateOnly(year, month, day);

        _clock.UtcNow = _clock
            .StartOfLocalDayUtc(localRunDate)
            .AddMinutes(30);

        DateTime dueYesterdayUtc = _clock
            .StartOfLocalDayUtc(localRunDate.AddDays(-1))
            .AddHours(12);

        Loan loan = SeedLoan(dueAtUtc: dueYesterdayUtc);

        bool changed = await Service().MarkLoanOverdueAsync(loan.Id);

        Assert.True(changed);
        Assert.Equal(LoanStatus.Overdue, loan.Status);
        Assert.Single(_events.Published.OfType<LoanOverdue>());
    }

    private sealed class UnusedPenaltyService : IPenaltyService
    {
        public Task<PenaltyApplicationResult> ApplyAsync(
            Loan loan,
            IReadOnlyList<ReturnPenaltyItem> items,
            DateTime nowUtc,
            CancellationToken ct = default) =>
            throw new NotSupportedException(
                "Overdue marking must not apply penalty points.");

        public Task<PenaltyRecalculationResult> RecalculateStatusAsync(
            Guid staffMemberId,
            DateTime nowUtc,
            CancellationToken ct = default) =>
            throw new NotSupportedException(
                "Loan processing must not recalculate staff penalties.");
    }
}