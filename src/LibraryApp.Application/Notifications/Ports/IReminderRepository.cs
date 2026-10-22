using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Ports;

/// <summary>
/// The Notifications data of the 09:00 due-soon and overdue reminders (NOT-05): templates and stored notifications. The
/// loans come from <see cref="Services.IReminderLoanReader"/>; this repository only adds to the Notifications table.
/// </summary>
public interface IReminderRepository
{
    /// <summary>The active templates of these types.</summary>
    Task<IReadOnlyList<ReminderTemplate>> GetTemplatesAsync(IReadOnlyCollection<NotificationType> types, CancellationToken ct = default);

    /// <summary>The loans that already have a notification of one of these types on this local date (NOT-06).</summary>
    Task<IReadOnlySet<(Guid LoanId, NotificationType Type)>> GetLoanNotificationsOnAsync(
        DateOnly localDate, IReadOnlyCollection<NotificationType> types, CancellationToken ct = default);

    /// <summary>
    /// The local date of the latest notification of each type for these loans, only dates before
    /// <paramref name="beforeDate"/>; a loan without one is not in the result.
    /// </summary>
    Task<IReadOnlyDictionary<(Guid LoanId, NotificationType Type), DateOnly>> GetLastLoanNotificationDatesAsync(
        IReadOnlyCollection<Guid> loanIds, IReadOnlyCollection<NotificationType> types, DateOnly beforeDate,
        CancellationToken ct = default);

    /// <summary>Adds a new notification; it is saved by the caller's unit of work.</summary>
    void Add(Notification notification);
}

public sealed record ReminderTemplate(NotificationType Type, string Subject, string BodyTemplate);
