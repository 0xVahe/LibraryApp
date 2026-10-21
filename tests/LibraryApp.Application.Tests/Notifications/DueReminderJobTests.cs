using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Application.Tests.Catalog;
using LibraryApp.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryApp.Application.Tests.Notifications;

/// <summary>NOT-05. October 2026: the 15th is a Thursday, the 19th a Monday.</summary>
public sealed class DueReminderJobTests
{
    private static readonly DateOnly Monday19 = new(2026, 10, 19);
    private static readonly DateOnly Thursday15 = new(2026, 10, 15);
    private const string ManagerEmail = "manager@ameriabank.am";

    private readonly FakeReminderRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private YerevanClock _clock = new(Monday19);

    public DueReminderJobTests()
    {
        _repository.Templates.Add(new ReminderTemplate(
            NotificationType.DueSoonReminder, "Due soon: {{BookTitle}}", "<p>{{EmployeeName}}, due {{DueDate}}</p>"));
        _repository.Templates.Add(new ReminderTemplate(
            NotificationType.OverdueNotice, "Overdue: {{BookTitle}}", "<p>{{EmployeeName}}, was due {{DueDate}}</p>"));
    }

    /// <summary>The stored Outlook rows: what the delivery job will send (NOT-03).</summary>
    private List<Notification> Emails => _repository.Stored.Where(n => n.Channel == NotificationChannel.Outlook).ToList();

    private Task<string> RunAsync(DateOnly today)
    {
        _clock = new YerevanClock(today);
        var job = new DueReminderJob(
            _repository, new NotificationTemplateRenderer(), _unitOfWork, new FakeIdGenerator(), _clock,
            new WeekdayCalendar(), NullLogger<DueReminderJob>.Instance);
        return job.RunAsync(new DailyJobRun(DueReminderJob.Name, today, null), default);
    }

    [Fact]
    public async Task Reminds_loans_due_in_2_working_days_on_both_channels()
    {
        ReminderLoan loan = _repository.AddLoan(DueLocal(2026, 10, 21), "Clean Code");   // Wednesday
        _repository.AddLoan(DueLocal(2026, 10, 20), "Tomorrow");
        _repository.AddLoan(DueLocal(2026, 10, 22), "In 3 days");

        await RunAsync(Monday19);

        Assert.Equal(2, _repository.Stored.Count);
        Assert.All(_repository.Stored, n =>
        {
            Assert.Equal(NotificationType.DueSoonReminder, n.Type);
            Assert.Equal(loan.LoanId, n.LoanId);
            Assert.Equal(Monday19, n.LocalDate);
            Assert.Null(n.EventId);
        });
        Assert.Equal(NotificationStatus.Sent, _repository.Stored.Single(n => n.Channel == NotificationChannel.InApp).Status);

        Notification email = Assert.Single(Emails);
        Assert.Equal(loan.CorporateEmail, email.RecipientEmail);
        Assert.Null(email.CcEmail);
        Assert.Equal("Due soon: Clean Code", email.Subject);
        Assert.Contains("21.10.2026", email.Body);
    }

    [Fact]
    public async Task The_email_is_left_Pending_for_the_delivery_job()
    {
        _repository.AddLoan(DueLocal(2026, 10, 21), "Clean Code");

        await RunAsync(Monday19);

        Notification email = Assert.Single(Emails);
        Assert.Equal(NotificationStatus.Pending, email.Status);
        Assert.Null(email.SentAtUtc);
        Assert.Null(email.NextAttemptAtUtc);   // due now
        Assert.Equal(1, _unitOfWork.Transactions);   // both rows of a reminder are saved together
    }

    [Fact]
    public async Task A_due_date_on_a_day_off_is_reminded_with_the_next_working_day()
    {
        // Thursday: 1 working day = Friday, 2 working days = Monday. Saturday, Sunday and Monday are reminded today.
        _repository.AddLoan(DueLocal(2026, 10, 16), "Friday");
        _repository.AddLoan(DueLocal(2026, 10, 17), "Saturday");
        _repository.AddLoan(DueLocal(2026, 10, 18), "Sunday");
        _repository.AddLoan(DueLocal(2026, 10, 19), "Monday");

        await RunAsync(Thursday15);

        Assert.Equal(["Due soon: Saturday", "Due soon: Sunday", "Due soon: Monday"], Emails.Select(n => n.Subject));
    }

    [Fact]
    public async Task Due_dates_are_Yerevan_dates_not_UTC_dates()
    {
        // 21 Oct 23:30 in Yerevan is still the 21st; 22 Oct 00:30 in Yerevan is 21 Oct 20:30 UTC.
        _repository.AddLoan(new DateTime(2026, 10, 21, 19, 30, 0, DateTimeKind.Utc), "Late on the 21st");
        _repository.AddLoan(new DateTime(2026, 10, 21, 20, 30, 0, DateTimeKind.Utc), "Early on the 22nd");

        await RunAsync(Monday19);

        Assert.Equal("Due soon: Late on the 21st", Assert.Single(Emails).Subject);
    }

    [Theory]
    [InlineData(2026, 10, 13, false)]   // 4 working days overdue
    [InlineData(2026, 10, 12, true)]    // 5
    [InlineData(2026, 10, 9, false)]    // 6
    [InlineData(2026, 10, 5, true)]     // 10
    public async Task Overdue_notice_every_5_working_days(int year, int month, int day, bool expected)
    {
        _repository.AddLoan(DueLocal(year, month, day), "Overdue book");

        await RunAsync(Monday19);

        Assert.Equal(expected, Emails.Any(n => n.Subject == "Overdue: Overdue book"));
    }

    [Fact]
    public async Task Line_manager_is_in_CC_after_14_working_days_overdue()
    {
        _repository.AddLoan(DueLocal(2026, 10, 5), "10 days", ManagerEmail);
        _repository.AddLoan(DueLocal(2026, 9, 28), "15 days", ManagerEmail);

        await RunAsync(Monday19);

        Assert.Null(Emails.Single(n => n.Subject == "Overdue: 10 days").CcEmail);
        Assert.Equal(ManagerEmail, Emails.Single(n => n.Subject == "Overdue: 15 days").CcEmail);
        Assert.All(
            _repository.Stored.Where(n => n.Channel == NotificationChannel.InApp),
            n => Assert.Null(n.CcEmail));
    }

    [Fact]
    public async Task A_second_run_on_the_same_day_stores_nothing_again()
    {
        _repository.AddLoan(DueLocal(2026, 10, 21), "Clean Code");
        _repository.AddLoan(DueLocal(2026, 10, 12), "Overdue book");

        await RunAsync(Monday19);
        string summary = await RunAsync(Monday19);

        Assert.Equal(2, Emails.Count);
        Assert.Equal(4, _repository.Stored.Count);
        Assert.EndsWith("stored 0, failed 0, skipped 2", summary);
    }

    [Fact]
    public async Task The_next_day_is_a_new_day()
    {
        _repository.AddLoan(DueLocal(2026, 10, 21), "Clean Code");
        await RunAsync(Monday19);

        // Tuesday: the loan is due in 1 working day, so no second due-soon reminder.
        await RunAsync(new DateOnly(2026, 10, 20));

        Assert.Single(Emails);
    }

    [Fact]
    public async Task Without_an_active_template_nothing_is_stored()
    {
        _repository.Templates.RemoveAll(t => t.Type == NotificationType.DueSoonReminder);
        _repository.AddLoan(DueLocal(2026, 10, 21), "Clean Code");

        string summary = await RunAsync(Monday19);

        Assert.Empty(_repository.Stored);
        Assert.EndsWith("stored 0, failed 1, skipped 0", summary);
    }

    [Fact]
    public async Task Summary_counts_each_kind()
    {
        _repository.AddLoan(DueLocal(2026, 10, 21), "Due soon");
        _repository.AddLoan(DueLocal(2026, 10, 12), "Overdue");

        string summary = await RunAsync(Monday19);

        Assert.Equal("due soon 1, overdue 1, stored 2, failed 0, skipped 0", summary);
    }

    /// <summary>Due at noon Yerevan time on that date.</summary>
    private static DateTime DueLocal(int year, int month, int day) =>
        new DateTime(year, month, day, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FakeReminderRepository : IReminderRepository
    {
        public List<ReminderTemplate> Templates { get; } = new();
        public List<ReminderLoan> Loans { get; } = new();
        public List<Notification> Stored { get; } = new();

        public ReminderLoan AddLoan(DateTime dueAtUtc, string title, string? lineManagerEmail = null)
        {
            var loan = new ReminderLoan(
                Guid.NewGuid(), Guid.NewGuid(), "Anna Petrosyan", "anna.petrosyan@ameriabank.am",
                lineManagerEmail, title, dueAtUtc);
            Loans.Add(loan);
            return loan;
        }

        public Task<IReadOnlyList<ReminderTemplate>> GetTemplatesAsync(
            IReadOnlyCollection<NotificationType> types, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReminderTemplate>>(Templates.Where(t => types.Contains(t.Type)).ToList());

        public Task<IReadOnlyList<ReminderLoan>> GetOpenLoansDueAsync(
            DateTime? dueFromUtc, DateTime dueBeforeUtc, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReminderLoan>>(Loans
                .Where(l => (dueFromUtc == null || l.DueAtUtc >= dueFromUtc) && l.DueAtUtc < dueBeforeUtc)
                .OrderBy(l => l.DueAtUtc)
                .ToList());

        public Task<IReadOnlySet<(Guid LoanId, NotificationType Type)>> GetLoanNotificationsOnAsync(
            DateOnly localDate, IReadOnlyCollection<NotificationType> types, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<(Guid, NotificationType)>>(Stored
                .Where(n => n.LocalDate == localDate && n.LoanId != null && types.Contains(n.Type))
                .Select(n => (n.LoanId!.Value, n.Type))
                .ToHashSet());

        public void Add(Notification notification) => Stored.Add(notification);
    }

    /// <summary>Monday to Friday, no holidays.</summary>
    private sealed class WeekdayCalendar : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        public DateOnly AddWorkingDays(DateOnly from, int days)
        {
            DateOnly date = from;
            while (days > 0)
            {
                date = date.AddDays(1);
                if (IsWorkingDay(date))
                    days--;
            }

            return date;
        }

        public int CountWorkingDays(DateOnly fromExclusive, DateOnly toInclusive)
        {
            int count = 0;
            for (DateOnly date = fromExclusive.AddDays(1); date <= toInclusive; date = date.AddDays(1))
            {
                if (IsWorkingDay(date))
                    count++;
            }

            return count;
        }
    }

    /// <summary>Asia/Yerevan is UTC+4 all year; "now" is 09:00 local on the given day.</summary>
    private sealed class YerevanClock(DateOnly today) : IClock
    {
        private static readonly TimeSpan Offset = TimeSpan.FromHours(4);

        public DateTime UtcNow { get; } = today.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc) - Offset;

        public DateOnly LocalToday => ToLocalDate(UtcNow);

        public DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);

        public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(utc + Offset);

        public DateTime StartOfLocalDayUtc(DateOnly localDate) =>
            localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - Offset;

        public DateTime EndOfLocalDayUtc(DateOnly localDate) => StartOfLocalDayUtc(localDate.AddDays(1)).AddTicks(-1);
    }
}