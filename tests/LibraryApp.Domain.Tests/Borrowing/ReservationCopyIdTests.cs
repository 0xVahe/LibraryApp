using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using Xunit;

namespace LibraryApp.Domain.Tests.Borrowing;

public class ReservationCopyIdTests
{
    private static readonly DateTime CreatedAtUtc =
        new(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc);

    private static Reservation CreateReservation()
    {
        return Reservation.Create(
            id: Guid.NewGuid(),
            bookId: Guid.NewGuid(),
            staffMemberId: Guid.NewGuid(),
            createdAtUtc: CreatedAtUtc,
            queuePosition: 1);
    }

    [Fact]
    public void Create_has_no_assigned_copy()
    {
        var reservation = CreateReservation();

        Assert.Null(reservation.CopyId);
        Assert.Equal(ReservationStatus.Waiting, reservation.Status);
    }

    [Fact]
    public void MarkReady_assigns_copy_and_leaves_waiting_queue()
    {
        var reservation = CreateReservation();
        var copyId = Guid.NewGuid();
        var deadline = CreatedAtUtc.AddDays(2);

        reservation.MarkReady(copyId, deadline);

        Assert.Equal(copyId, reservation.CopyId);
        Assert.Equal(deadline, reservation.ReadyUntilUtc);
        Assert.Equal(ReservationStatus.ReadyForPickup, reservation.Status);
        Assert.Equal(0, reservation.QueuePosition);
    }

    [Fact]
    public void MarkReady_rejects_empty_copy_id_without_changing_reservation()
    {
        var reservation = CreateReservation();

        Assert.Throws<DomainException>(() =>
            reservation.MarkReady(Guid.Empty, CreatedAtUtc.AddDays(2)));

        Assert.Null(reservation.CopyId);
        Assert.Null(reservation.ReadyUntilUtc);
        Assert.Equal(ReservationStatus.Waiting, reservation.Status);
        Assert.Equal(1, reservation.QueuePosition);
    }

    [Fact]
    public void Cancel_preserves_assigned_copy_id()
    {
        var reservation = CreateReservation();
        var copyId = Guid.NewGuid();

        reservation.MarkReady(copyId, CreatedAtUtc.AddDays(2));

        reservation.Cancel();

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(copyId, reservation.CopyId);
    }

    [Fact]
    public void Expire_preserves_assigned_copy_id()
    {
        var reservation = CreateReservation();
        var copyId = Guid.NewGuid();
        var deadline = CreatedAtUtc.AddDays(2);

        reservation.MarkReady(copyId, deadline);

        // Expiration requires a time strictly after the deadline.
        reservation.Expire(deadline.AddSeconds(1));

        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.Equal(copyId, reservation.CopyId);
    }

    [Fact]
    public void Expire_at_exact_deadline_is_rejected_without_changing_reservation()
    {
        var reservation = CreateReservation();
        var copyId = Guid.NewGuid();
        var deadline = CreatedAtUtc.AddDays(2);
        reservation.MarkReady(copyId, deadline);

        DomainException exception = Assert.Throws<DomainException>(
            () => reservation.Expire(deadline));

        Assert.Equal(BorrowingErrors.PickupDeadlineNotPassed.Code, exception.Error.Code);
        Assert.Equal(ReservationStatus.ReadyForPickup, reservation.Status);
        Assert.Equal(copyId, reservation.CopyId);
    }

    [Fact]
    public void Fulfil_preserves_assigned_copy_id()
    {
        var reservation = CreateReservation();
        var copyId = Guid.NewGuid();

        reservation.MarkReady(copyId, CreatedAtUtc.AddDays(2));

        reservation.Fulfil();

        Assert.Equal(ReservationStatus.Fulfilled, reservation.Status);
        Assert.Equal(copyId, reservation.CopyId);
    }
}
