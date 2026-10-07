using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Tests.Fakes;

/// <summary>
/// In-memory stand-in for Group 2's Borrowing read service. It tracks active and historical loans in memory
/// and follows the read rules agreed for Group 2 queries. Used in unit tests.
/// </summary>
public sealed class InMemoryBorrowingReadService : IBorrowingReadService
{
    private readonly Dictionary<Guid, OpenLoanInfo> _openLoans = new();
    private readonly List<LoanRecord> _historicalLoans = new();

    /// <summary>Test switch: when set, the next read operation throws it once.
    /// Used to test error handling when the Borrowing service call fails.</summary>
    public Exception? ThrowOnNextCall { get; set; }

    // ---------- Test Helpers (Not part of IBorrowingReadService) ----------

    /// <summary>Adds an active/open loan directly into the fake store.</summary>
    public OpenLoanInfo AddOpenLoan(
        Guid staffMemberId,
        Guid bookCopyId,
        Guid bookId,
        DateTime dueAtUtc,
        LoanStatus status = LoanStatus.Active)
    {
        var loan = new OpenLoanInfo(Guid.NewGuid(), bookCopyId, bookId, dueAtUtc, status);
        _openLoans[loan.LoanId] = loan;

        // Keep track in history for summary calculations
        _historicalLoans.Add(new LoanRecord(loan.LoanId, staffMemberId, bookCopyId, bookId, dueAtUtc, status, IsClosed: false));
        return loan;
    }

    /// <summary>Adds a closed/returned loan to test summary statistics and history filters.</summary>
    public void AddClosedLoan(Guid staffMemberId, Guid bookCopyId, Guid bookId, DateTime dueAtUtc)
    {
        _historicalLoans.Add(new LoanRecord(
            LoanId: Guid.NewGuid(),
            StaffMemberId: staffMemberId,
            BookCopyId: bookCopyId,
            BookId: bookId,
            DueAtUtc: dueAtUtc,
            Status: LoanStatus.Returned,
            IsClosed: true));
    }

    /// <summary>Simulates closing/returning a loan in the fake store.</summary>
    public bool CloseLoan(Guid loanId)
    {
        if (!_openLoans.Remove(loanId)) return false;

        var index = _historicalLoans.FindIndex(l => l.LoanId == loanId);
        if (index != -1)
        {
            var loan = _historicalLoans[index];
            _historicalLoans[index] = loan with { IsClosed = true, Status = LoanStatus.Returned };
        }

        return true;
    }

    /// <summary>Clears all in-memory loans.</summary>
    public void Clear()
    {
        _openLoans.Clear();
        _historicalLoans.Clear();
    }

    // ---------- IBorrowingReadService Implementation ----------

    // BR-MEM-4: Returns all active/open loans for a specific staff member.
    public Task<IReadOnlyList<OpenLoanInfo>> GetOpenLoansAsync(Guid staffMemberId, CancellationToken ct = default)
    {
        CheckThrowOnNextCall();

        var openLoansForStaff = _historicalLoans
            .Where(l => l.StaffMemberId == staffMemberId && !l.IsClosed)
            .Select(l => _openLoans[l.LoanId])
            .ToList();

        return Task.FromResult<IReadOnlyList<OpenLoanInfo>>(openLoansForStaff);
    }

    // MEM-08: Computes staff borrowing metrics for "My library" view.
    public Task<StaffBorrowingSummary> GetStaffSummaryAsync(Guid staffMemberId, CancellationToken ct = default)
    {
        CheckThrowOnNextCall();

        var staffLoans = _historicalLoans.Where(l => l.StaffMemberId == staffMemberId).ToList();
        var activeLoansCount = staffLoans.Count(l => !l.IsClosed);
        var overdueLoansCount = staffLoans.Count(l => !l.IsClosed && l.DueAtUtc < DateTime.UtcNow);
        var totalBorrowedCount = staffLoans.Count;

        var summary = new StaffBorrowingSummary(
            staffMemberId,
            activeLoansCount,
            overdueLoansCount,
            totalBorrowedCount);

        return Task.FromResult(summary);
    }

    // CAT-08: Finds the earliest upcoming due date among the provided book copy IDs.
    public Task<DateTime?> GetNextDueDateAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default)
    {
        CheckThrowOnNextCall();

        if (copyIds.Count == 0)
        {
            return Task.FromResult<DateTime?>(null);
        }

        var earliestDueDate = _openLoans.Values
            .Where(l => copyIds.Contains(l.BookCopyId))
            .OrderBy(l => l.DueAtUtc)
            .Select(l => (DateTime?)l.DueAtUtc)
            .FirstOrDefault();

        return Task.FromResult(earliestDueDate);
    }

    // ---------- Private Helpers ----------

    private void CheckThrowOnNextCall()
    {
        if (ThrowOnNextCall is not null)
        {
            var failure = ThrowOnNextCall;
            ThrowOnNextCall = null; // Throws only once
            throw failure;
        }
    }

    private sealed record LoanRecord(
        Guid LoanId,
        Guid StaffMemberId,
        Guid BookCopyId,
        Guid BookId,
        DateTime DueAtUtc,
        LoanStatus Status,
        bool IsClosed);
}