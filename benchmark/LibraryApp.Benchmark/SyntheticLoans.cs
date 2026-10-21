using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Benchmark;

/// <summary>
/// Returned loans spread over the last year, only in the free time of each copy: a copy is busy while any loan already in
/// the database is open on it (from its issue to its return, or until now), so no loan of a copy overlaps another loan of
/// the same copy, as in a real library. Members are drawn at random. A fixed random seed makes every database with the
/// same staff members, copies and loans look the same.
/// </summary>
public static class SyntheticLoans
{
    private const int LoanDays = 14;
    private static readonly TimeSpan History = TimeSpan.FromDays(365);

    /// <param name="busy">Per copy, the periods taken by the loans already in the database.</param>
    public static IReadOnlyList<Loan> Create(
        int count,
        long firstNumber,
        IReadOnlyList<Guid> members,
        IReadOnlyList<Guid> copies,
        IReadOnlyDictionary<Guid, IReadOnlyList<(DateTime From, DateTime To)>> busy,
        DateTime nowUtc)
    {
        var random = new Random(2026);
        var start = nowUtc - History;
        var end = nowUtc.AddDays(-1);

        var loans = new List<Loan>(count);
        for (var copyIndex = 0; copyIndex < copies.Count; copyIndex++)
        {
            // The loans of this copy: the i-th loan goes to copy i % copies.Count.
            var forCopy = count / copies.Count + (copyIndex < count % copies.Count ? 1 : 0);
            if (forCopy == 0)
                continue;

            var copy = copies[copyIndex];
            var free = FreePeriods(start, end, busy.TryGetValue(copy, out var taken) ? taken : []);
            foreach (var (from, to, loansInPeriod) in Share(free, forCopy))
            {
                var slot = (to - from) / loansInPeriod;
                for (var j = 0; j < loansInPeriod; j++)
                {
                    // Issued in the first 20 % of the slot and kept for 30 % to 75 % of it: returned by 95 % of the slot,
                    // so never after the next issue of the copy.
                    var issuedAt = from + slot * j + slot * (random.NextDouble() * 0.2);
                    var returnedAt = issuedAt + slot * (0.3 + random.NextDouble() * 0.45);

                    var loan = Loan.Issue(
                        new Guid($"{BenchmarkDatabase.IdPrefix}{firstNumber + loans.Count:D12}"),
                        copy,
                        members[random.Next(members.Count)],
                        Truncate(issuedAt),
                        LoanDays);
                    loan.Return(Truncate(returnedAt));
                    loans.Add(loan);
                }
            }
        }

        return loans;
    }

    /// <summary>The parts of [start, end] not covered by a busy period, oldest first.</summary>
    private static List<(DateTime From, DateTime To)> FreePeriods(
        DateTime start, DateTime end, IReadOnlyList<(DateTime From, DateTime To)> taken)
    {
        var free = new List<(DateTime From, DateTime To)>();
        var cursor = start;
        foreach (var (from, to) in taken.OrderBy(p => p.From))
        {
            if (from > cursor)
                free.Add((cursor, from < end ? from : end));
            if (to > cursor)
                cursor = to;
            if (cursor >= end)
                break;
        }

        if (cursor < end)
            free.Add((cursor, end));

        return free.Where(p => p.To > p.From).ToList();
    }

    /// <summary>Shares the loans of a copy among its free periods in proportion to their length (largest remainder).</summary>
    private static IEnumerable<(DateTime From, DateTime To, int Loans)> Share(List<(DateTime From, DateTime To)> free, int loans)
    {
        if (free.Count == 0)
            throw new BenchmarkException("a copy has no free time in the last year for synthetic loans.");

        var total = free.Sum(p => (p.To - p.From).Ticks);
        var exact = free.Select(p => (double)(p.To - p.From).Ticks / total * loans).ToArray();
        var whole = exact.Select(e => (int)Math.Floor(e)).ToArray();
        foreach (var i in Enumerable.Range(0, free.Count).OrderByDescending(i => exact[i] - whole[i]).Take(loans - whole.Sum()))
            whole[i]++;

        for (var i = 0; i < free.Count; i++)
        {
            if (whole[i] > 0)
                yield return (free[i].From, free[i].To, whole[i]);
        }
    }

    // Whole seconds, UTC.
    private static DateTime Truncate(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}
