using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Tests.Borrowing;

public sealed class BorrowingLifecycleTests
{
    private static readonly DateTime IssuedAtUtc =
        new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);

    private static Loan CreateLoan()
    {
        return Loan.Issue(
            id: Guid.NewGuid(),
            bookCopyId: Guid.NewGuid(),
            staffMemberId: Guid.NewGuid(),
            issuedAtUtc: IssuedAtUtc,
            loanDays: 21);
    }

    private static Reservation CreateReservation()
    {
        return Reservation.Create(
            id: Guid.NewGuid(),
            bookId: Guid.NewGuid(),
            staffMemberId: Guid.NewGuid(),
            createdAtUtc: IssuedAtUtc,
            queuePosition: 1);
    }

    private static PenaltyRecord CreatePenalty()
    {
        return PenaltyRecord.Create(
            id: Guid.NewGuid(),
            staffMemberId: Guid.NewGuid(),
            loanId: Guid.NewGuid(),
            points: 4,
            reason: "Returned four working days late.",
            createdAtUtc: IssuedAtUtc);
    }

    [Fact]
    public void Issue_creates_an_active_loan()
    {
        Loan loan = CreateLoan();

        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Equal(0, loan.RenewalCount);
        Assert.Null(loan.ReturnedAtUtc);
        Assert.Equal(IssuedAtUtc.AddDays(21), loan.DueAtUtc);
        Assert.True(loan.IsOpen);
    }

    [Fact]
    public void Renew_extends_the_deadline_and_increments_count()
    {
        Loan loan = CreateLoan();
        DateTime originalDueAtUtc = loan.DueAtUtc;

        loan.Renew(7);

        Assert.Equal(originalDueAtUtc.AddDays(7), loan.DueAtUtc);
        Assert.Equal(1, loan.RenewalCount);
        Assert.Equal(LoanStatus.Active, loan.Status);
    }

    [Fact]
    public void Renew_rejects_an_overdue_loan()
    {
        Loan loan = CreateLoan();
        loan.MarkOverdue(loan.DueAtUtc.AddTicks(1));

        DomainException exception =
            Assert.Throws<DomainException>(() => loan.Renew(7));

        Assert.Equal(
            BorrowingErrors.LoanCannotBeRenewed,
            exception.Error);
    }

    [Fact]
    public void MarkOverdue_changes_active_loan_to_overdue()
    {
        Loan loan = CreateLoan();

        loan.MarkOverdue(loan.DueAtUtc.AddTicks(1));

        Assert.Equal(LoanStatus.Overdue, loan.Status);
        Assert.True(loan.IsOpen);
    }

    [Fact]
    public void Return_changes_an_active_loan_to_returned()
    {
        Loan loan = CreateLoan();
        DateTime returnedAtUtc = IssuedAtUtc.AddDays(2);

        loan.Return(returnedAtUtc);

        Assert.Equal(LoanStatus.Returned, loan.Status);
        Assert.Equal(returnedAtUtc, loan.ReturnedAtUtc);
        Assert.False(loan.IsOpen);
    }

    [Fact]
    public void Returned_loan_cannot_be_returned_again()
    {
        Loan loan = CreateLoan();
        loan.Return(IssuedAtUtc.AddDays(2));

        DomainException exception =
            Assert.Throws<DomainException>(
                () => loan.Return(IssuedAtUtc.AddDays(3)));

        Assert.Equal(
            BorrowingErrors.LoanAlreadyClosed,
            exception.Error);
    }

    [Fact]
    public void New_reservation_is_waiting_at_position_one()
    {
        Reservation reservation = CreateReservation();

        Assert.Equal(ReservationStatus.Waiting, reservation.Status);
        Assert.Equal(1, reservation.QueuePosition);
        Assert.Null(reservation.ReadyUntilAtUtc);
        Assert.True(reservation.IsActive);
    }

    [Fact]
    public void MarkReady_removes_reservation_from_waiting_queue()
    {
        Reservation reservation = CreateReservation();
        DateTime readyUntilAtUtc = IssuedAtUtc.AddDays(3);

        reservation.MarkReady(readyUntilAtUtc);

        Assert.Equal(
            ReservationStatus.ReadyForPickup,
            reservation.Status);

        Assert.Equal(0, reservation.QueuePosition);
        Assert.Equal(readyUntilAtUtc, reservation.ReadyUntilAtUtc);
    }

    [Fact]
    public void Waiting_reservation_cannot_be_fulfilled()
    {
        Reservation reservation = CreateReservation();

        DomainException exception =
            Assert.Throws<DomainException>(
                () => reservation.Fulfil());

        Assert.Equal(
            BorrowingErrors.ReservationNotReady,
            exception.Error);

        Assert.Equal(ReservationStatus.Waiting, reservation.Status);
    }

    [Fact]
    public void Ready_reservation_can_be_fulfilled()
    {
        Reservation reservation = CreateReservation();

        reservation.MarkReady(IssuedAtUtc.AddDays(3));
        reservation.Fulfil();

        Assert.Equal(
            ReservationStatus.Fulfilled,
            reservation.Status);

        Assert.Equal(0, reservation.QueuePosition);
        Assert.False(reservation.IsActive);
    }

    [Fact]
    public void Waiting_reservation_can_be_cancelled()
    {
        Reservation reservation = CreateReservation();

        reservation.Cancel();

        Assert.Equal(
            ReservationStatus.Cancelled,
            reservation.Status);

        Assert.Equal(0, reservation.QueuePosition);
        Assert.False(reservation.IsActive);
    }

    [Fact]
    public void New_penalty_is_active_and_counts()
    {
        PenaltyRecord penalty = CreatePenalty();

        Assert.Equal(
            PenaltyRecordStatus.Active,
            penalty.Status);

        Assert.Equal(4, penalty.Points);
        Assert.Equal(
            IssuedAtUtc.AddDays(90),
            penalty.ExpiresAtUtc);

        Assert.True(
            penalty.CountsAt(IssuedAtUtc.AddDays(1)));
    }

    [Fact]
    public void Penalty_rejects_points_above_ten()
    {
        DomainException exception =
            Assert.Throws<DomainException>(() =>
                PenaltyRecord.Create(
                    id: Guid.NewGuid(),
                    staffMemberId: Guid.NewGuid(),
                    loanId: Guid.NewGuid(),
                    points: 11,
                    reason: "Late return.",
                    createdAtUtc: IssuedAtUtc));

        Assert.Equal(
            BorrowingErrors.InvalidPenaltyPoints,
            exception.Error);
    }

    [Fact]
    public void Waive_preserves_points_but_stops_counting()
    {
        PenaltyRecord penalty = CreatePenalty();

        penalty.Waive("The return was recorded incorrectly.");

        Assert.Equal(
            PenaltyRecordStatus.Waived,
            penalty.Status);

        Assert.Equal(4, penalty.Points);

        Assert.Equal(
            "The return was recorded incorrectly.",
            penalty.WaivedReason);

        Assert.False(
            penalty.CountsAt(IssuedAtUtc.AddDays(1)));
    }

    [Fact]
    public void Waive_requires_a_reason()
    {
        PenaltyRecord penalty = CreatePenalty();

        DomainException exception =
            Assert.Throws<DomainException>(
                () => penalty.Waive("   "));

        Assert.Equal(
            BorrowingErrors.WaiverReasonRequired,
            exception.Error);

        Assert.Equal(
            PenaltyRecordStatus.Active,
            penalty.Status);
    }

    [Fact]
    public void Penalty_expires_at_exact_expiry_timestamp()
    {
        PenaltyRecord penalty = CreatePenalty();

        penalty.Expire(penalty.ExpiresAtUtc);

        Assert.Equal(
            PenaltyRecordStatus.Expired,
            penalty.Status);

        Assert.False(
            penalty.CountsAt(penalty.ExpiresAtUtc));
    }
}