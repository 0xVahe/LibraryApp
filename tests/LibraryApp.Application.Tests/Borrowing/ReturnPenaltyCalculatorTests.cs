using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Borrowing;
using Xunit;

namespace LibraryApp.Application.Tests.Borrowing;

public sealed class ReturnPenaltyCalculatorTests
{
    [Theory]
    [InlineData(15)] // Before the due date.
    [InlineData(16)] // On the due date.
    public void Return_on_or_before_due_date_has_no_penalty(int returnDay)
    {
        ReturnPenaltyCalculator calculator = CreateCalculator();

        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(16, 8),
            Utc(returnDay, 12),
            damaged: false);

        Assert.Equal(0, result.OverdueWorkingDays);
        Assert.Empty(result.Penalties);
        Assert.Equal(0, result.TotalPoints);
    }

    /// <summary>
    /// The daily job may mark a loan Overdue on its due date (a due time just after midnight, a catch-up run). With
    /// NextLocalDay the dates alone say "not late"; the stored Overdue status still makes the return late (BQ-BA-03).
    /// </summary>
    [Fact]
    public void A_loan_already_marked_overdue_returned_on_its_due_date_gets_a_zero_point_record()
    {
        ReturnPenaltyCalculator calculator = CreateCalculator();

        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(16, 8),
            Utc(16, 12),
            damaged: false,
            alreadyOverdue: true);

        Assert.Equal(0, result.OverdueWorkingDays);
        Assert.Equal(0, Assert.Single(result.Penalties).Points);
    }

    [Theory]
    [InlineData(OverdueFromMode.NextLocalDay, 16, 19, 59, false)]   // 23:59 local on the due date
    [InlineData(OverdueFromMode.NextLocalDay, 16, 20, 1, true)]     // 00:01 local on the next day
    [InlineData(OverdueFromMode.DueTime, 16, 8, 0, false)]          // exactly the due time
    [InlineData(OverdueFromMode.DueTime, 16, 8, 1, true)]           // one minute after
    public void The_overdue_rule_follows_the_setting(OverdueFromMode mode, int day, int hour, int minute, bool expected)
    {
        var settings = new BorrowingOptions { Loans = new LoanOptions { OverdueFrom = mode } };
        var rule = new OverdueRule(new TestClock(), Microsoft.Extensions.Options.Options.Create(settings));

        Assert.Equal(expected, rule.IsOverdue(Utc(16, 8), Utc(day, hour, minute)));
    }

    [Fact]
    public void Late_return_excludes_due_date_and_weekend()
    {
        ReturnPenaltyCalculator calculator = CreateCalculator();

        // Friday -> Tuesday: Monday and Tuesday count.
        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(16),
            Utc(20),
            damaged: false);

        Assert.Equal(2, result.OverdueWorkingDays);
        Assert.Equal(2, Assert.Single(result.Penalties).Points);
    }

    [Fact]
    public void Late_return_excludes_configured_holiday()
    {
        var calendar = new TestCalendar(new DateOnly(2026, 10, 19));
        ReturnPenaltyCalculator calculator =
            CreateCalculator(calendar: calendar);

        // Monday is a test holiday, so only Tuesday counts.
        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(16),
            Utc(20),
            damaged: false);

        Assert.Equal(1, result.OverdueWorkingDays);
        Assert.Equal(1, Assert.Single(result.Penalties).Points);
    }

    [Fact]
    public void Late_points_use_configured_rate_and_cap()
    {
        ReturnPenaltyCalculator calculator = CreateCalculator(
            configure: settings =>
            {
                settings.Penalties.PointsPerLateWorkingDay = 2;
                settings.Penalties.MaxLatePoints = 3;
            });

        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(16),
            Utc(20),
            damaged: false);

        // Two working days at two points, capped at three.
        Assert.Equal(2, result.OverdueWorkingDays);
        Assert.Equal(3, Assert.Single(result.Penalties).Points);
    }

    [Fact]
    public void On_time_damaged_return_uses_configured_damage_points()
    {
        ReturnPenaltyCalculator calculator = CreateCalculator(
            configure: settings =>
                settings.Penalties.DamagedPoints = 7);

        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(16),
            Utc(16),
            damaged: true);

        Assert.Equal(0, result.OverdueWorkingDays);
        Assert.Equal(7, Assert.Single(result.Penalties).Points);
    }

    [Fact]
    public void Sum_creates_separate_late_and_damage_penalties()
    {
        ReturnPenaltyCalculator calculator = CreateCalculator();

        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(16),
            Utc(20),
            damaged: true);

        Assert.Equal(2, result.OverdueWorkingDays);
        Assert.Equal(2, result.Penalties.Count);
        Assert.Contains(result.Penalties, penalty => penalty.Points == 2);
        Assert.Contains(result.Penalties, penalty => penalty.Points == 5);
        Assert.Equal(7, result.TotalPoints);
    }

    [Theory]
    [InlineData(20, 5)] // Late: 2; damage: 5.
    [InlineData(26, 6)] // Late: 6; damage: 5.
    [InlineData(23, 5)] // Equal penalties: only one record.
    public void Highest_creates_one_penalty(
        int returnDay,
        int expectedPoints)
    {
        ReturnPenaltyCalculator calculator = CreateCalculator(
            configure: settings =>
                settings.Penalties.LateAndDamaged =
                    LateAndDamagedMode.Highest);

        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(16),
            Utc(returnDay),
            damaged: true);

        Assert.Equal(
            expectedPoints,
            Assert.Single(result.Penalties).Points);

        Assert.Equal(expectedPoints, result.TotalPoints);
    }

    /// <summary>Requirements, section 8: a late return always creates a penalty record, with 0 points when no working day passed.</summary>
    [Fact]
    public void Weekend_only_lateness_still_creates_a_zero_point_record()
    {
        ReturnPenaltyCalculator calculator = CreateCalculator();

        // Friday -> Sunday: overdue, but no working days elapsed.
        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(16),
            Utc(18),
            damaged: false);

        Assert.Equal(0, result.OverdueWorkingDays);
        Assert.Equal(0, Assert.Single(result.Penalties).Points);
    }

    [Theory]
    [InlineData(OverdueFromMode.NextLocalDay, false)]
    [InlineData(OverdueFromMode.DueTime, true)]
    public void Same_local_day_after_due_time_respects_overdue_mode(
        OverdueFromMode mode,
        bool expectsPenalty)
    {
        ReturnPenaltyCalculator calculator = CreateCalculator(
            configure: settings => settings.Loans.OverdueFrom = mode);

        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(16, 8),
            Utc(16, 9),
            damaged: false);

        Assert.Equal(0, result.OverdueWorkingDays);

        if (expectsPenalty)
            Assert.Equal(0, Assert.Single(result.Penalties).Points);
        else
            Assert.Empty(result.Penalties);
    }

    [Fact]
    public void Local_midnight_starts_new_day_even_on_same_utc_date()
    {
        ReturnPenaltyCalculator calculator = CreateCalculator();

        // UTC Sunday 19:59 = local Sunday 23:59.
        // UTC Sunday 20:00 = local Monday 00:00.
        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(18, 19, 59),
            Utc(18, 20),
            damaged: false);

        Assert.Equal(1, result.OverdueWorkingDays);
        Assert.Equal(1, Assert.Single(result.Penalties).Points);
    }

    [Fact]
    public void Utc_midnight_does_not_start_a_new_local_day()
    {
        ReturnPenaltyCalculator calculator = CreateCalculator();

        // Both timestamps are on Monday locally.
        ReturnPenaltyCalculation result = calculator.Calculate(
            Utc(18, 21),
            Utc(19, 1),
            damaged: false);

        Assert.Equal(0, result.OverdueWorkingDays);
        Assert.Empty(result.Penalties);
    }

    [Fact]
    public void Non_utc_timestamps_are_rejected()
    {
        ReturnPenaltyCalculator calculator = CreateCalculator();
        DateTime unspecified =
            DateTime.SpecifyKind(Utc(16), DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() =>
            calculator.Calculate(unspecified, Utc(20), false));

        Assert.Throws<ArgumentException>(() =>
            calculator.Calculate(Utc(16), unspecified, false));
    }

    private static ReturnPenaltyCalculator CreateCalculator(
        Action<BorrowingOptions>? configure = null,
        IWorkingDayCalendar? calendar = null)
    {
        var settings = new BorrowingOptions
        {
            Penalties = new PenaltyOptions
            {
                PointsPerLateWorkingDay = 1,
                MaxLatePoints = 10,
                DamagedPoints = 5,
                LostPoints = 10,
                LifetimeDays = 90,
                RestrictedFromPoints = 5,
                SuspendedFromPoints = 10,
                SuspensionDays = 30,
                AfterSuspension = AfterSuspensionMode.RestrictedByPoints,
                LateAndDamaged = LateAndDamagedMode.Sum
            },
            Loans = new LoanOptions
            {
                OverdueFrom = OverdueFromMode.NextLocalDay
            }
        };

        configure?.Invoke(settings);

        return new ReturnPenaltyCalculator(
            new PenaltyPolicy(settings.Penalties.ToRules()),
            calendar ?? new TestCalendar(),
            new TestClock(),
            Microsoft.Extensions.Options.Options.Create(settings));
    }

    private static DateTime Utc(
        int day,
        int hour = 12,
        int minute = 0) =>
        new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private sealed class TestClock : IClock
    {
        // Fixed UTC+4 for these October 2026 test cases.
        public DateTime UtcNow => Utc(20);

        public DateOnly LocalToday => ToLocalDate(UtcNow);

        public DateTime ToLocal(DateTime utc) =>
            DateTime.SpecifyKind(
                utc.AddHours(4),
                DateTimeKind.Unspecified);

        public DateOnly ToLocalDate(DateTime utc) =>
            DateOnly.FromDateTime(ToLocal(utc));

        public DateTime StartOfLocalDayUtc(DateOnly localDate) =>
            DateTime.SpecifyKind(
                localDate.ToDateTime(TimeOnly.MinValue).AddHours(-4),
                DateTimeKind.Utc);

        public DateTime EndOfLocalDayUtc(DateOnly localDate) =>
            StartOfLocalDayUtc(localDate.AddDays(1)).AddTicks(-1);
    }

    private sealed class TestCalendar(
        params DateOnly[] holidays) : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) =>
            date.DayOfWeek is not DayOfWeek.Saturday
                and not DayOfWeek.Sunday
            && !holidays.Contains(date);

        public int CountWorkingDays(
            DateOnly fromExclusive,
            DateOnly toInclusive)
        {
            int count = 0;

            for (DateOnly date = fromExclusive.AddDays(1);
                 date <= toInclusive;
                 date = date.AddDays(1))
            {
                if (IsWorkingDay(date))
                    count++;
            }

            return count;
        }

        public DateOnly AddWorkingDays(DateOnly from, int days)
        {
            if (days < 0)
                throw new ArgumentOutOfRangeException(nameof(days));

            DateOnly date = from;

            while (days > 0)
            {
                date = date.AddDays(1);

                if (IsWorkingDay(date))
                    days--;
            }

            return date;
        }
    }
}