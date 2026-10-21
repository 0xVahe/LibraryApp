using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using Xunit;

namespace LibraryApp.Application.Tests.Borrowing;

/// <summary>
/// <see cref="OverdueRule.OverdueIfDueBeforeUtc"/> is the query form of <see cref="OverdueRule.IsOverdue"/>: for every due
/// moment the two give the same answer, in both settings of <c>Borrowing:Loans:OverdueFrom</c>, around the local midnight
/// of Asia/Yerevan (UTC+4).
/// </summary>
public sealed class OverdueRuleTests
{
    // 20 Oct 2026 10:30 in Yerevan.
    private static readonly DateTime Now = new(2026, 10, 20, 6, 30, 0, DateTimeKind.Utc);

    private static OverdueRule Rule(OverdueFromMode mode) =>
        new(new FixedYerevanClock(Now), Microsoft.Extensions.Options.Options.Create(new BorrowingOptions { Loans = new LoanOptions { OverdueFrom = mode } }));

    public static TheoryData<OverdueFromMode, DateTime> DueMoments()
    {
        var data = new TheoryData<OverdueFromMode, DateTime>();
        DateTime[] dues =
        [
            new(2026, 10, 19, 19, 59, 59, DateTimeKind.Utc),   // 19 Oct 23:59:59 local
            new(2026, 10, 19, 20, 0, 0, DateTimeKind.Utc),     // 20 Oct 00:00 local
            new(2026, 10, 20, 6, 29, 59, DateTimeKind.Utc),    // a second before now
            Now,
            new(2026, 10, 20, 6, 30, 1, DateTimeKind.Utc),     // a second after now
            new(2026, 10, 20, 19, 59, 59, DateTimeKind.Utc),   // the end of today, local
            new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc),
        ];
        foreach (OverdueFromMode mode in Enum.GetValues<OverdueFromMode>())
            foreach (DateTime due in dues)
                data.Add(mode, due);
        return data;
    }

    [Theory]
    [MemberData(nameof(DueMoments))]
    public void The_query_bound_agrees_with_the_rule(OverdueFromMode mode, DateTime dueAtUtc)
    {
        OverdueRule rule = Rule(mode);

        Assert.Equal(rule.IsOverdue(dueAtUtc, Now), dueAtUtc < rule.OverdueIfDueBeforeUtc(Now));
    }

    [Fact]
    public void Next_local_day_is_overdue_from_the_local_midnight_and_due_time_from_now()
    {
        Assert.Equal(new DateTime(2026, 10, 19, 20, 0, 0, DateTimeKind.Utc), Rule(OverdueFromMode.NextLocalDay).OverdueIfDueBeforeUtc(Now));
        Assert.Equal(Now, Rule(OverdueFromMode.DueTime).OverdueIfDueBeforeUtc(Now));
    }
}
