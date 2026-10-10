using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Borrowing;

/// <summary>
/// Defines stable errors for the Borrowing module.
/// </summary>
public static class BorrowingErrors
{
    /// <summary>A copy barcode is required to issue a loan.</summary>
    public static readonly Error BarcodeRequired = Error.Validation(
        "borrowing.barcode_required",
        "A copy barcode is required.");
    
    
    /// <summary>No copy has the barcode that was scanned.</summary>
    public static readonly Error CopyNotFound =
        Error.NotFound("borrowing.copy_not_found", "No copy with this barcode was found.");

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
    
    
    // Reservation errors

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
}