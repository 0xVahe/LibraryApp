using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Tests.Fakes;

public sealed class InMemoryBorrowingReadService : IBorrowingReadService
{
    private readonly Dictionary<Guid, OpenLoanInfo> _openLoans = new();
    private readonly List<LoanRecord> _historicalLoans = new();

    public Exception? ThrowOnNextCall { get; set; }

    public OpenLoanInfo AddOpenLoan(
        Guid staffMemberId,
        Guid bookCopyId,
        Guid bookId,
        DateTime dueAtUtc,
        LoanStatus status = LoanStatus.Active)
    {
        var loan = new OpenLoanInfo(Guid.NewGuid(), bookCopyId, bookId, dueAtUtc, status);
        _openLoans[loan.LoanId] = loan;

        _historicalLoans.Add(new LoanRecord(loan.LoanId, staffMemberId, bookCopyId, bookId, dueAtUtc, status, IsClosed: false));
        return loan;
    }

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

    public void Clear()
    {
        _openLoans.Clear();
        _historicalLoans.Clear();
    }

    public Task<IReadOnlyList<OpenLoanInfo>> GetOpenLoansAsync(Guid staffMemberId, CancellationToken ct = default)
    {
        CheckThrowOnNextCall();

        var openLoansForStaff = _historicalLoans
            .Where(l => l.StaffMemberId == staffMemberId && !l.IsClosed)
            .Select(l => _openLoans[l.LoanId])
            .ToList();

        return Task.FromResult<IReadOnlyList<OpenLoanInfo>>(openLoansForStaff);
    }

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

    private void CheckThrowOnNextCall()
    {
        if (ThrowOnNextCall is not null)
        {
            var failure = ThrowOnNextCall;
            ThrowOnNextCall = null;
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