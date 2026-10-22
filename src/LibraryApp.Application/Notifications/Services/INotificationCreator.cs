using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Services;

/// <summary>
/// Creates the notifications of one event for one staff member: resolves the recipient, loads and renders the active
/// template, and stores one row per channel. Used by the event handlers; it never sends anything.
/// </summary>
public interface INotificationCreator
{
    /// <summary>
    /// Stores one row per requested channel for the event (G4-05): an Outlook row is Pending and sent later from the stored
    /// row (NOT-08); an in-app row has nothing to send, so it is stored as Sent. The channel of each type comes from the
    /// notification catalogue (requirements, section 9).
    /// Must run inside the caller's unit of work (event handlers do). An event that already created this type is ignored (G4-09).
    /// <paramref name="loanId"/> is stored when the notification is about a loan; then a channel that already has this
    /// type for this loan on the same Yerevan day is skipped (NOT-06, G4-02).
    /// With <paramref name="ccLineManager"/> the recipient's line manager is put in CC of the Outlook row (when the
    /// recipient has one); in-app rows have no CC.
    /// With <paramref name="calendarItem"/> (iCalendar text, see <see cref="CalendarItemBuilder"/>) the Outlook row carries the
    /// calendar item as an attachment (NOT-07); in-app rows have none.
    /// {{EmployeeName}} is added from the recipient; <paramref name="values"/> carries the other placeholders.
    /// </summary>
    /// <exception cref="Domain.Common.DomainException">The recipient or the active template does not exist.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="channels"/> is empty or has an undefined channel, or the template needs a placeholder that has no value.
    /// </exception>
    Task CreateAsync(
        NotificationType type,
        Guid staffMemberId,
        Guid eventId,
        IReadOnlyCollection<NotificationChannel> channels,
        IReadOnlyDictionary<string, string> values,
        Guid? loanId = null,
        bool ccLineManager = false,
        string? calendarItem = null,
        CancellationToken ct = default);
}
