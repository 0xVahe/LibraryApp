using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Borrowing;

/// <summary>
/// Defines stable errors for the Borrowing module.
/// </summary>
public static class BorrowingErrors
{
    /// <summary>No loan has this id.</summary>
    public static readonly Error LoanNotFound =
        Error.NotFound("borrowing.loan_not_found", "The loan does not exist.");

    /// <summary>No copy has the barcode that was scanned.</summary>
    public static readonly Error CopyNotFound =
        Error.NotFound("borrowing.copy_not_found", "No copy with this barcode was found.");

    /// <summary>The copy exists but has no open (Active or Overdue) loan: it is on the shelf, held or already returned.</summary>
    public static readonly Error NoOpenLoanForCopy =
        Error.NotFound("borrowing.no_open_loan_for_copy", "This copy is not on loan.");

    /// <summary>The copy exists but cannot be lent now (on loan, reserved, under repair, lost or withdrawn).</summary>
    public static readonly Error CopyNotAvailable =
        Error.BusinessRule("borrowing.copy_not_available", "This copy is not available for lending.");

    /// <summary>
    /// A required reference identifier is empty.
    /// </summary>
    public static readonly Error InvalidReferenceId = Error.Validation(
        "borrowing.invalid_reference_id",
        "A referenced entity identifier must not be empty.");

    /// <summary>
    /// A supplied timestamp is not explicitly UTC.
    /// </summary>
    public static readonly Error TimestampMustBeUtc = Error.Validation(
        "borrowing.timestamp_must_be_utc",
        "The timestamp must be expressed in UTC.");

    /// <summary>
    /// The loan duration or renewal extension is not positive.
    /// </summary>
    public static readonly Error InvalidLoanDays = Error.Validation(
        "borrowing.invalid_loan_days",
        "The loan duration must be greater than zero.");

    /// <summary>
    /// A renewal was requested for a loan that is not active.
    /// </summary>
    public static readonly Error LoanCannotBeRenewed = Error.BusinessRule(
        "borrowing.loan_cannot_be_renewed",
        "Only an active loan can be renewed.");

    /// <summary>
    /// An overdue transition was requested for a loan that is not active.
    /// </summary>
    public static readonly Error LoanCannotBecomeOverdue = Error.BusinessRule(
        "borrowing.loan_cannot_become_overdue",
        "Only an active loan can become overdue.");

    /// <summary>
    /// The loan deadline has not yet passed.
    /// </summary>
    public static readonly Error LoanNotPastDue = Error.BusinessRule(
        "borrowing.loan_not_past_due",
        "The loan deadline has not passed.");

    /// <summary>
    /// A return or lost declaration was requested for a closed loan.
    /// </summary>
    public static readonly Error LoanAlreadyClosed = Error.BusinessRule(
        "borrowing.loan_already_closed",
        "The loan is already closed.");

    /// <summary>
    /// A closing timestamp precedes the issue timestamp.
    /// </summary>
    public static readonly Error LoanClosingBeforeIssue = Error.Validation(
        "borrowing.loan_closing_before_issue",
        "A loan cannot be closed before it was issued.");

    public static readonly Error ReturnNoteTooLong = Error.Validation(
        "borrowing.return_note_too_long",
        "The return note may have at most 500 characters.");


    // Reservation errors

    /// <summary>An available copy exists, so this book does not need to be reserved.</summary>
    public static readonly Error ReservationCopyAvailable = Error.BusinessRule(
        "borrowing.reservation_copy_available",
        "A copy of this book is available; reservation is not required.");

    /// <summary>The employee already has the maximum number of active reservations.</summary>
    public static Error ReservationLimitReached(int maxActive) => Error.BusinessRule(
        "borrowing.reservation_limit_reached",
        $"The employee already has the maximum of {maxActive} active reservations.");

    /// <summary>The employee already has an active reservation for this book (a conflict with the current queue).</summary>
    public static readonly Error DuplicateActiveReservation = Error.Conflict(
        "borrowing.duplicate_active_reservation",
        "The employee already has an active reservation for this book.");

    /// <summary>The employee already holds an open loan for this book.</summary>
    public static readonly Error BookAlreadyOnLoan = Error.BusinessRule(
        "borrowing.book_already_on_loan",
        "The employee already holds this book through an open loan.");

    /// <summary>No reservation has this id, or it belongs to another staff member (NFR-03).</summary>
    public static readonly Error ReservationNotFound = Error.NotFound(
        "borrowing.reservation_not_found",
        "The reservation does not exist.");

    /// <summary>A Staff Member may reserve only for themselves (NFR-03).</summary>
    public static readonly Error ReservationForAnotherStaffMember = Error.Forbidden(
        "borrowing.reservation_for_another_staff_member",
        "A staff member can reserve books only for themselves.");

    /// <summary>A Librarian or Library Administrator must name the staff member the reservation is for.</summary>
    public static readonly Error ReservationStaffMemberRequired = Error.Validation(
        "borrowing.reservation_staff_member_required",
        "Name the staff member the reservation is for.");

    /// <summary>Restricted employees cannot make reservations.</summary>
    public static readonly Error RestrictedReservationForbidden = Error.BusinessRule(
        "borrowing.restricted_reservation_forbidden",
        "Restricted employees cannot make reservations.");

    /// <summary>
    /// A waiting reservation requires a positive queue position.
    /// </summary>
    public static readonly Error InvalidQueuePosition = Error.Validation(
        "borrowing.invalid_queue_position",
        "A waiting reservation must have a positive queue position.");

    /// <summary>
    /// The requested operation requires a waiting reservation.
    /// </summary>
    public static readonly Error ReservationNotWaiting = Error.BusinessRule(
        "borrowing.reservation_not_waiting",
        "Only a waiting reservation can perform this operation.");

    /// <summary>
    /// The requested operation requires a reservation ready for pickup.
    /// </summary>
    public static readonly Error ReservationNotReady = Error.BusinessRule(
        "borrowing.reservation_not_ready",
        "Only a reservation ready for pickup can perform this operation.");

    /// <summary>
    /// A closed reservation cannot be cancelled.
    /// </summary>
    public static readonly Error ReservationCannotBeCancelled = Error.BusinessRule(
        "borrowing.reservation_cannot_be_cancelled",
        "Only a waiting or ready reservation can be cancelled.");

    /// <summary>
    /// The pickup deadline must be after reservation creation.
    /// </summary>
    public static readonly Error InvalidPickupDeadline = Error.Validation(
        "borrowing.invalid_pickup_deadline",
        "The pickup deadline must be after reservation creation.");

    /// <summary>
    /// A reservation cannot expire before its pickup deadline has passed.
    /// </summary>
    public static readonly Error PickupDeadlineNotPassed = Error.BusinessRule(
        "borrowing.pickup_deadline_not_passed",
        "The pickup deadline has not passed.");


    // Penalty errors

    /// <summary>
    /// Penalty points must remain within the agreed range.
    /// </summary>
    public static readonly Error InvalidPenaltyPoints = Error.Validation(
        "borrowing.invalid_penalty_points",
        "Penalty points must be between zero and ten.");

    /// <summary>A penalty record would stop counting at or before the moment it is created.</summary>
    public static readonly Error PenaltyExpiryNotAfterCreation = Error.Validation(
        "borrowing.penalty_expiry_not_after_creation",
        "A penalty must expire after it is created.");

    /// <summary>
    /// A penalty requires an explanation.
    /// </summary>
    public static readonly Error PenaltyReasonRequired = Error.Validation(
        "borrowing.penalty_reason_required",
        "A reason is required for the penalty.");

    /// <summary>
    /// A waiver requires an explanation.
    /// </summary>
    public static readonly Error WaiverReasonRequired = Error.Validation(
        "borrowing.waiver_reason_required",
        "A reason is required to waive the penalty.");

    /// <summary>
    /// The requested operation requires an active penalty.
    /// </summary>
    public static readonly Error PenaltyNotActive = Error.BusinessRule(
        "borrowing.penalty_not_active",
        "Only an active penalty can perform this operation.");

    /// <summary>
    /// A penalty cannot expire before its expiry timestamp.
    /// </summary>
    public static readonly Error PenaltyNotDueToExpire = Error.BusinessRule(
        "borrowing.penalty_not_due_to_expire",
        "The penalty has not reached its expiry timestamp.");

    public static readonly Error PenaltyNotFound = new(
    "Borrowing.PenaltyNotFound",
    "The specified penalty record was not found.");
}
