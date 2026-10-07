using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Tests.Borrowing.Fakes;

public sealed class FakeLoanRepository : ILoanRepository
{
    private readonly List<Loan> _saved = new();
    private readonly List<Loan> _pending = new();

    public IReadOnlyList<Loan> Saved => _saved;
    public IReadOnlyList<Loan> Pending => _pending;

    public int CountCalls { get; private set; }
    public int AddCalls { get; private set; }

    // Prepares existing loans before a test starts.
    public void Seed(Loan loan) => _saved.Add(loan);

    public Task<int> CountOpenLoansAsync(
        Guid staffMemberId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        CountCalls++;

        int count = _saved.Count(loan =>
            loan.StaffMemberId == staffMemberId && loan.IsOpen);

        return Task.FromResult(count);
    }

    public Task AddAsync(Loan loan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(loan);

        AddCalls++;
        _pending.Add(loan);

        return Task.CompletedTask;
    }

    public void Commit()
    {
        _saved.AddRange(_pending);
        _pending.Clear();
    }

    public void Rollback() => _pending.Clear();
}