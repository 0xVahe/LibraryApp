namespace LibraryApp.Domain.Notifications;

/// <summary>
/// Kind of notification, from the notification catalogue of the requirements document (section 9).
/// Explicit values starting at 1 (SD-11); never renumber, only add at the end.
/// AccessRestricted and AccessSuspended are two types (G4-04).
/// </summary>
public enum NotificationType
{
    /// <summary>Staff member enrolled (StaffEnrolled).</summary>
    Welcome = 1,

    /// <summary>Loan issued, includes the due date (LoanIssued).</summary>
    LoanConfirmation = 2,

    /// <summary>Daily job: loan due in 2 working days.</summary>
    DueSoonReminder = 3,

    /// <summary>LoanOverdue, then every 5 working days; line manager in CC after 14 days.</summary>
    OverdueNotice = 4,

    /// <summary>Reserved book is ready for pickup (ReservationReady).</summary>
    ReservationReady = 5,

    /// <summary>Penalty points added and the new total (PenaltyApplied).</summary>
    PenaltyApplied = 6,

    /// <summary>Staff status changed to Restricted (StaffStatusChanged).</summary>
    AccessRestricted = 7,

    /// <summary>Staff status changed to Suspended; line manager informed (StaffStatusChanged).</summary>
    AccessSuspended = 8,

    /// <summary>Outlook calendar item (.ics) on the due date (LoanIssued / LoanRenewed).</summary>
    DueDateCalendarItem = 9,

    /// <summary>Book returned (LoanReturned).</summary>
    ReturnReceipt = 10,

    /// <summary>Security notice: a staff member changed their password (StaffPasswordChanged).</summary>
    PasswordChanged = 11,
}
