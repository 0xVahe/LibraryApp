using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Benchmark;

/// <summary>
/// Returned loans spread over the last year. Each copy gets its own time line of slots, so loans of one copy never
/// overlap, as in a real library; members are drawn at random. A fixed random seed makes every database seeded with the
/// same staff members and copies look the same.
/// </summary>
public static class SyntheticLoans
{
    private const int LoanDays = 14;
    private static readonly TimeSpan History = TimeSpan.FromDays(365);

    public static IReadOnlyList<Loan> Create(int count, long firstNumber, IReadOnlyList<Guid> members, IReadOnlyList<Guid> copies, DateTime nowUtc)
    {
        var random = new Random(2026);
        var start = nowUtc - History;
        var end = nowUtc.AddDays(-1);
        var perCopy = (int)Math.Ceiling(count / (double)copies.Count);
        var slot = (end - start) / perCopy;

        var loans = new List<Loan>(count);
        for (var i = 0; i < count; i++)
        {
            var copyIndex = i % copies.Count;
            var slotIndex = i / copies.Count;

            // Issued in the first fifth of the slot, returned after 30 % to 95 % of it: never after the next issue.
            var issuedAt = start + slot * slotIndex + slot * (random.NextDouble() * 0.2);
            var returnedAt = issuedAt + slot * (0.3 + random.NextDouble() * 0.65);

            var loan = Loan.Issue(
                new Guid($"{BenchmarkDatabase.IdPrefix}{firstNumber + i:D12}"),
                copies[copyIndex],
                members[random.Next(members.Count)],
                Truncate(issuedAt),
                LoanDays);
            loan.Return(Truncate(returnedAt));
            loans.Add(loan);
        }

        return loans;
    }

    // Whole seconds, UTC.
    private static DateTime Truncate(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}
