using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using LibraryApp.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// The Borrowing seed run on every day of a year, weekends and public holidays included, at a different hour each day:
/// 40 loans with 6 overdue on the seeding day and still 12 days later (REP-02 acceptance), the lending rules, and a penalty
/// record for every late return ("a return from Overdue always creates a PenaltyRecord"). Each run is only tracked, never
/// saved, so every day starts from the same Catalog and Staff seed.
/// <para>
/// The year is split into four quarters, one test class each: xUnit runs test classes in parallel, each with its own host
/// and database, so the 365 runs take about a quarter of the time. Together the quarters cover every day of the year
/// (<see cref="BorrowingSeedCalendarQuartersTests"/>).
/// </para>
/// </summary>
public abstract class BorrowingSeedCalendarTests(ApiHostWithoutBorrowingSeed factory, int quarter)
{
    private const int StableDays = 12;

    /// <summary>1-4: the quarter of 2026 this class runs the seed on.</summary>
    public int Quarter => quarter;

    /// <summary>The days the test runs the seed on: every day of <paramref name="quarter"/> (1-4) of 2026.</summary>
    public static IEnumerable<DateOnly> DaysOf(int quarter)
    {
        var first = new DateOnly(2026, (quarter - 1) * 3 + 1, 1);
        for (var day = first; day < first.AddMonths(3); day = day.AddDays(1))
            yield return day;
    }

    [Fact]
    public async Task Every_day_of_the_quarter_gives_40_loans_with_6_overdue_for_12_days_and_follows_the_rules()
    {
        var failures = new List<string>();
        var timeZone = factory.Services.GetRequiredService<IOptions<TimeZoneOptions>>();

        foreach (DateOnly day in DaysOf(Quarter))
        {
            // 04:00 Yerevan = 00:00 UTC; the hour moves through the whole day over the year.
            var seededAt = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(day.DayOfYear % 24 - 4);
            var clock = new SystemClock(new FixedTime(seededAt), timeZone);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var seeder = ActivatorUtilities.CreateInstance<BorrowingSeeder>(scope.ServiceProvider, clock);
            await seeder.SeedAsync(default);

            List<Loan> loans = db.ChangeTracker.Entries<Loan>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            List<PenaltyRecord> penalties = db.ChangeTracker.Entries<PenaltyRecord>().Select(e => e.Entity).ToList();
            failures.AddRange((await CheckAsync(scope.ServiceProvider, clock, loans, penalties)).Select(f => $"{day:yyyy-MM-dd ddd} (seeded at {seededAt:yyyy-MM-dd HH:mm}Z): {f}"));
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)));
    }

    private static async Task<List<string>> CheckAsync(IServiceProvider services, IClock clock, List<Loan> loans, List<PenaltyRecord> penalties)
    {
        var failures = new List<string>();
        DateOnly today = clock.LocalToday;
        List<Loan> open = loans.Where(l => l.Status is LoanStatus.Active or LoanStatus.Overdue).ToList();

        // REP-02 counts a loan as overdue when it is open and its local due date is before the day of the report.
        int OverdueOn(DateOnly reportDay) => open.Count(l => l.DueAtUtc < clock.StartOfLocalDayUtc(reportDay));

        if (loans.Count != 40)
            failures.Add($"{loans.Count} loans instead of 40");
        for (int after = 0; after <= StableDays; after++)
        {
            int overdue = OverdueOn(today.AddDays(after));
            if (overdue != 6)
                failures.Add($"{overdue} overdue loans {after} days after seeding instead of 6");
        }

        if (open.Any(l => (l.Status == LoanStatus.Overdue) != (l.DueAtUtc < clock.StartOfLocalDayUtc(today))))
            failures.Add("an open loan's status does not match its due date");
        if (loans.Any(l => l.IssuedAtUtc > clock.UtcNow || l.ReturnedAtUtc > clock.UtcNow || l.ReturnedAtUtc < l.IssuedAtUtc))
            failures.Add("a loan is issued or returned in the future, or returned before it was issued");
        if (open.GroupBy(l => l.BookCopyId).Any(g => g.Count() > 1))
            failures.Add("two open loans share a copy");

        var db = services.GetRequiredService<LibraryDbContext>();
        foreach (var member in open.GroupBy(l => l.StaffMemberId))
        {
            StaffMemberStatus status = await db.StaffMembers.AsNoTracking().Where(m => m.Id == member.Key).Select(m => m.Status).SingleAsync();
            int maxLoans = (await services.GetRequiredService<IStaffMemberService>().GetLendingRulesAsync(member.Key)).MaxLoans;
            int limit = status switch
            {
                StaffMemberStatus.Active => maxLoans,
                StaffMemberStatus.Restricted => 1,   // BR-MEM-3
                _ => 0,                              // Suspended, Deactivated (BR-MEM-4)
            };
            if (member.Count() > limit)
                failures.Add($"staff member {member.Key} ({status}) holds {member.Count()} open loans, limit {limit}");
        }

        foreach (Loan returned in loans.Where(l => l.Status == LoanStatus.Returned))
        {
            bool late = clock.ToLocalDate(returned.ReturnedAtUtc!.Value) > clock.ToLocalDate(returned.DueAtUtc);
            bool hasPenalty = penalties.Any(p => p.LoanId == returned.Id);
            if (late != hasPenalty)
                failures.Add($"loan {returned.Id}: late {late}, penalty record {hasPenalty}");
        }

        return failures;
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

public class BorrowingSeedCalendarQ1Tests(ApiHostWithoutBorrowingSeed factory)
    : BorrowingSeedCalendarTests(factory, 1), IClassFixture<ApiHostWithoutBorrowingSeed>;

public class BorrowingSeedCalendarQ2Tests(ApiHostWithoutBorrowingSeed factory)
    : BorrowingSeedCalendarTests(factory, 2), IClassFixture<ApiHostWithoutBorrowingSeed>;

public class BorrowingSeedCalendarQ3Tests(ApiHostWithoutBorrowingSeed factory)
    : BorrowingSeedCalendarTests(factory, 3), IClassFixture<ApiHostWithoutBorrowingSeed>;

public class BorrowingSeedCalendarQ4Tests(ApiHostWithoutBorrowingSeed factory)
    : BorrowingSeedCalendarTests(factory, 4), IClassFixture<ApiHostWithoutBorrowingSeed>;

/// <summary>The four quarter classes together run the seed on every day of 2026, each day once.</summary>
public class BorrowingSeedCalendarQuartersTests
{
    [Fact]
    public void The_quarters_cover_every_day_of_the_year_once()
    {
        List<DateOnly> days = Enumerable.Range(1, 4).SelectMany(BorrowingSeedCalendarTests.DaysOf).ToList();

        Assert.Equal(365, days.Count);
        Assert.Equal(days.Count, days.Distinct().Count());
        Assert.Equal(new DateOnly(2026, 1, 1), days.Min());
        Assert.Equal(new DateOnly(2026, 12, 31), days.Max());
    }

    [Fact]
    public void Each_quarter_has_exactly_one_test_class()
    {
        // The host is not used to read the quarter, so the classes are created without one.
        int[] quarters = typeof(BorrowingSeedCalendarTests).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(BorrowingSeedCalendarTests)))
            .Select(t => ((BorrowingSeedCalendarTests)Activator.CreateInstance(t, [null])!).Quarter)
            .Order().ToArray();

        Assert.Equal([1, 2, 3, 4], quarters);
    }
}
