using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace LibraryApp.Application.Notifications.Services;

/// <summary>
/// NOT-05: the 09:00 job on working days (schedule <c>Jobs:Schedules:notifications-due-reminders</c>).
/// <list type="bullet">
/// <item>DueSoonReminder: open loans due in 2 working days. A due date on a day off is reminded together with the next
/// working day, so every loan gets its reminder once.</item>
/// <item>OverdueNotice: every 5 working days overdue (5, 10, 15, ...); the first notice comes from the LoanOverdue event.
/// After 14 working days the line manager is in CC.</item>
/// </list>
/// Each reminder is stored as an in-app notification (delivered when stored) and a Pending Outlook notification, which the
/// delivery job sends (NOT-03). A loan that already has the same type today is skipped (NOT-06), so a run that is
/// repeated stores and sends nothing twice.
/// </summary>
public sealed class DueReminderJob(
    IReminderRepository reminders,
    INotificationTemplateRenderer renderer,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    IWorkingDayCalendar calendar,
    ILogger<DueReminderJob> logger) : IDailyJob
{
    /// <summary>Stored with every run: never rename.</summary>
    public const string Name = "notifications-due-reminders";

    public const int DueSoonWorkingDays = 2;
    public const int OverdueEveryWorkingDays = 5;
    public const int LineManagerCcAfterWorkingDays = 14;

    private static readonly NotificationType[] Types = [NotificationType.DueSoonReminder, NotificationType.OverdueNotice];

    public async Task<string> RunAsync(DailyJobRun run, CancellationToken ct)
    {
        DateOnly today = run.LocalDate;

        Dictionary<NotificationType, ReminderTemplate> templates =
            (await reminders.GetTemplatesAsync(Types, ct)).GroupBy(t => t.Type).ToDictionary(g => g.Key, g => g.First());
        IReadOnlySet<(Guid LoanId, NotificationType Type)> alreadySent =
            await reminders.GetLoanNotificationsOnAsync(today, Types, ct);

        List<Reminder> due = [];
        due.AddRange(await DueSoonAsync(today, ct));
        due.AddRange(await OverdueAsync(today, ct));

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

        return $"due soon {due.Count(r => r.Type == NotificationType.DueSoonReminder)}, " +
               $"overdue {due.Count(r => r.Type == NotificationType.OverdueNotice)}, " +
               $"stored {counts.Stored}, failed {counts.Failed}, skipped {counts.Skipped}";
    }

    private async Task<IEnumerable<Reminder>> DueSoonAsync(DateOnly today, CancellationToken ct)
    {
        // Due after the next working day, up to and including the working day after it.
        DateOnly afterDate = calendar.AddWorkingDays(today, DueSoonWorkingDays - 1);
        DateOnly lastDate = calendar.AddWorkingDays(today, DueSoonWorkingDays);

        IReadOnlyList<ReminderLoan> loans = await reminders.GetOpenLoansDueAsync(
            clock.StartOfLocalDayUtc(afterDate.AddDays(1)), clock.StartOfLocalDayUtc(lastDate.AddDays(1)), ct);

        return loans.Select(loan => new Reminder(NotificationType.DueSoonReminder, loan, CcEmail: null));
    }

    private async Task<IEnumerable<Reminder>> OverdueAsync(DateOnly today, CancellationToken ct)
    {
        // Overdue from the day after the due date: the whole due date is allowed (same rule as Borrowing).
        IReadOnlyList<ReminderLoan> loans = await reminders.GetOpenLoansDueAsync(null, clock.StartOfLocalDayUtc(today), ct);

        return loans
            .Select(loan => (Loan: loan, Days: calendar.CountWorkingDays(clock.ToLocalDate(loan.DueAtUtc), today)))
            .Where(x => x.Days > 0 && x.Days % OverdueEveryWorkingDays == 0)
            .Select(x => new Reminder(
                NotificationType.OverdueNotice,
                x.Loan,
                x.Days > LineManagerCcAfterWorkingDays ? x.Loan.LineManagerEmail : null));
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