using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.Options;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Notifications.Services;

/// <summary>
/// NOT-05: the 09:00 job on working days (schedule <c>Jobs:Schedules:notifications-due-reminders</c>); the numbers of
/// working days are the settings <c>Notifications:Reminders</c>.
/// <list type="bullet">
/// <item>DueSoonReminder: open loans due within <c>DueSoonWorkingDays</c> (2) that have not had one for this due date.
/// A due date on a day off is reminded together with the next working day, so every loan gets its reminder once.</item>
/// <item>OverdueNotice: loans that Borrowing has marked Overdue, every <c>OverdueEveryWorkingDays</c> (5) working days
/// overdue (5, 10, 15, ...); the first notice comes from the LoanOverdue event. After
/// <c>LineManagerCcAfterWorkingDays</c> (14) the line manager is in CC.</item>
/// </list>
/// A reminder is due until the loan has it, not only on its exact day: a day the job did not run (or failed) is caught
/// up at the next run, with one reminder for the latest step.
/// Each reminder is stored as an in-app notification (delivered when stored) and a Pending Outlook notification, which the
/// delivery job sends (NOT-03). A loan that already has the same type today is skipped (NOT-06), so a run that is
/// repeated stores and sends nothing twice. A run in which a reminder failed throws at the end, so the daily job runner
/// tries it again.
/// </summary>
public sealed class DueReminderJob(
    IReminderRepository reminders,
    INotificationTemplateRenderer renderer,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    IWorkingDayCalendar calendar,
    IOptions<NotificationsOptions> options,
    ILogger<DueReminderJob> logger) : IDailyJob
{
    /// <summary>Stored with every run: never rename.</summary>
    public const string Name = "notifications-due-reminders";

    private static readonly NotificationType[] Types = [NotificationType.DueSoonReminder, NotificationType.OverdueNotice];

    private readonly RemindersOptions _settings = options.Value.Reminders;

    public async Task<string> RunAsync(DailyJobRun run, CancellationToken ct)
    {
        DateOnly today = run.LocalDate;

        Dictionary<NotificationType, ReminderTemplate> templates =
            (await reminders.GetTemplatesAsync(Types, ct)).GroupBy(t => t.Type).ToDictionary(g => g.Key, g => g.First());
        IReadOnlySet<(Guid LoanId, NotificationType Type)> alreadySent =
            await reminders.GetLoanNotificationsOnAsync(today, Types, ct);

        IReadOnlyList<ReminderLoan> dueSoon = await DueSoonLoansAsync(today, ct);
        IReadOnlyList<ReminderLoan> overdue = await reminders.GetOverdueLoansAsync(ct);
        IReadOnlyDictionary<(Guid LoanId, NotificationType Type), DateOnly> lastSent =
            await reminders.GetLastLoanNotificationDatesAsync(
                dueSoon.Concat(overdue).Select(loan => loan.LoanId).ToHashSet(), Types, today, ct);

        List<Reminder> due = [];
        due.AddRange(DueSoon(dueSoon, lastSent));
        due.AddRange(Overdue(overdue, lastSent, today));

        var counts = new Counts();
        foreach (Reminder reminder in due)
        {
            if (alreadySent.Contains((reminder.Loan.LoanId, reminder.Type)))
            {
                counts.Skipped++;
                continue;
            }

            if (!templates.TryGetValue(reminder.Type, out ReminderTemplate? template))
            {
                logger.LogWarning("No active {Type} template: reminder for loan {LoanId} not sent", reminder.Type, reminder.Loan.LoanId);
                counts.Failed++;
                continue;
            }

            await StoreAsync(reminder, template, today, counts, ct);
        }

        string summary = $"due soon {due.Count(r => r.Type == NotificationType.DueSoonReminder)}, " +
                         $"overdue {due.Count(r => r.Type == NotificationType.OverdueNotice)}, " +
                         $"stored {counts.Stored}, failed {counts.Failed}, skipped {counts.Skipped}";

        // The stored reminders stay; the runner's next attempt skips them (NOT-06) and tries the failed ones again.
        if (counts.Failed > 0)
            throw new InvalidOperationException($"Some reminders could not be stored: {summary}");

        return summary;
    }

    /// <summary>Open loans due after today, up to and including the last working day of the due-soon period.</summary>
    private Task<IReadOnlyList<ReminderLoan>> DueSoonLoansAsync(DateOnly today, CancellationToken ct)
    {
        DateOnly lastDate = calendar.AddWorkingDays(today, _settings.DueSoonWorkingDays);
        return reminders.GetOpenLoansDueAsync(
            clock.StartOfLocalDayUtc(today.AddDays(1)), clock.StartOfLocalDayUtc(lastDate.AddDays(1)), ct);
    }

    private IEnumerable<Reminder> DueSoon(
        IEnumerable<ReminderLoan> loans, IReadOnlyDictionary<(Guid LoanId, NotificationType Type), DateOnly> lastSent) =>
        // Already reminded for this due date: a reminder sent within the due-soon period before it. One sent earlier was
        // for a due date before a renewal.
        loans
            .Where(loan => !lastSent.TryGetValue((loan.LoanId, NotificationType.DueSoonReminder), out DateOnly sentOn)
                           || calendar.CountWorkingDays(sentOn, clock.ToLocalDate(loan.DueAtUtc)) > _settings.DueSoonWorkingDays)
            .Select(loan => new Reminder(NotificationType.DueSoonReminder, loan, CcEmail: null));

    private IEnumerable<Reminder> Overdue(
        IEnumerable<ReminderLoan> loans,
        IReadOnlyDictionary<(Guid LoanId, NotificationType Type), DateOnly> lastSent,
        DateOnly today)
    {
        int every = _settings.OverdueEveryWorkingDays;
        foreach (ReminderLoan loan in loans)
        {
            DateOnly dueDate = clock.ToLocalDate(loan.DueAtUtc);
            int days = calendar.CountWorkingDays(dueDate, today);
            if (days < every)
                continue;

            // Due unless a notice was sent on or after the latest step (5, 10, 15, ... working days overdue).
            int step = days - days % every;
            if (lastSent.TryGetValue((loan.LoanId, NotificationType.OverdueNotice), out DateOnly sentOn)
                && calendar.CountWorkingDays(dueDate, sentOn) >= step)
                continue;

            yield return new Reminder(
                NotificationType.OverdueNotice,
                loan,
                days > _settings.LineManagerCcAfterWorkingDays ? loan.LineManagerEmail : null);
        }
    }

    private async Task StoreAsync(Reminder reminder, ReminderTemplate template, DateOnly today, Counts counts, CancellationToken ct)
    {
        ReminderLoan loan = reminder.Loan;
        RenderedNotification rendered;
        try
        {
            rendered = renderer.Render(template.Subject, template.BodyTemplate, new Dictionary<string, string>
            {
                [NotificationPlaceholders.EmployeeName] = loan.EmployeeName,
                [NotificationPlaceholders.BookTitle] = loan.BookTitle,
                [NotificationPlaceholders.DueDate] = NotificationPlaceholders.FormatDate(clock.ToLocalDate(loan.DueAtUtc)),
            });
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "{Type} template could not be rendered for loan {LoanId}", reminder.Type, loan.LoanId);
            counts.Failed++;
            return;
        }

        DateTime now = clock.UtcNow;
        Notification inApp = Create(reminder, NotificationChannel.InApp, rendered, now, today);
        Notification outlook = Create(reminder, NotificationChannel.Outlook, rendered, now, today);
        inApp.MarkSent(now);   // in-app: stored = delivered

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(token =>
            {
                reminders.Add(inApp);
                reminders.Add(outlook);
                return Task.CompletedTask;
            }, ct);
        }
        catch (DomainException ex) when (ex.Error == CommonErrors.UniqueViolation)
        {
            counts.Skipped++;   // another instance stored it a moment ago (NOT-06)
            return;
        }

        counts.Stored++;
    }

    private Notification Create(Reminder reminder, NotificationChannel channel, RenderedNotification rendered, DateTime now, DateOnly today) =>
        Notification.Create(
            ids.NewId(),
            reminder.Loan.StaffMemberId,
            reminder.Type,
            channel,
            reminder.Loan.CorporateEmail,
            channel == NotificationChannel.Outlook ? reminder.CcEmail : null,
            rendered.Subject,
            rendered.HtmlBody,
            now,
            today,
            loanId: reminder.Loan.LoanId);

    private sealed record Reminder(NotificationType Type, ReminderLoan Loan, string? CcEmail);

    private sealed class Counts
    {
        public int Stored { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
    }
}
