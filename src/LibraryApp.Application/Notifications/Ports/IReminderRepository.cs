using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Ports;

/// <summary>
/// Data of the 09:00 due-soon and overdue reminders (NOT-05). Reads loans, staff and books without tracking, in one query
/// per list (no query per loan); only adds to the Notifications table.
/// </summary>
public interface IReminderRepository
{
    /// <summary>The active templates of these types.</summary>
    Task<IReadOnlyList<ReminderTemplate>> GetTemplatesAsync(IReadOnlyCollection<NotificationType> types, CancellationToken ct = default);

    /// <summary>
    /// Open loans (Active or Overdue) due from <paramref name="dueFromUtc"/> and before <paramref name="dueBeforeUtc"/>,
    /// earliest first. Loans of staff who left the bank (Deactivated) are left out.
    /// </summary>
    Task<IReadOnlyList<ReminderLoan>> GetOpenLoansDueAsync(DateTime dueFromUtc, DateTime dueBeforeUtc, CancellationToken ct = default);

    /// <summary>
    /// Loans that Borrowing has marked Overdue (its rule <c>Borrowing:Loans:OverdueFrom</c>), earliest due first. Loans of
    /// staff who left the bank (Deactivated) are left out.
    /// </summary>
    Task<IReadOnlyList<ReminderLoan>> GetOverdueLoansAsync(CancellationToken ct = default);

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

public sealed record ReminderLoan(
    Guid LoanId,
    Guid StaffMemberId,
    string EmployeeName,
    string CorporateEmail,
    string? LineManagerEmail,
    string BookTitle,
    DateTime DueAtUtc);