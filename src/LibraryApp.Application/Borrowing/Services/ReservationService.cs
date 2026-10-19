using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// Reservations (BOR-07). Creating one runs in one transaction under the staff lock and the book's queue lock
/// (<see cref="BorrowingLocks"/>, in that order), so the limits of the employee and the queue positions of the book are
/// decided one request at a time; the unique index on active reservations is the last line of defence (409).
/// The input is validated by <c>CreateReservationRequestValidator</c> before the endpoint is reached (SD-15).
/// </summary>
public sealed class ReservationService(
    IStaffMemberService staffMembers,
    ICatalogService catalog,
    ILoanRepository loans,
    IReservationRepository reservations,
    ReservationQueueService reservationQueue,
    IUnitOfWork unitOfWork,
    IApplicationLock applicationLock,
    ICurrentUser currentUser,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<BorrowingOptions> options) : IReservationService
{
    private readonly ReservationOptions settings = options.Value.Reservations;

    public async Task<ReservationDto> CreateAsync(CreateReservationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guid staffMemberId = ResolveStaffMember(request.StaffMemberId);
        Guid bookId = request.BookId;

        Reservation reservation = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // A deleted book is gone for readers: the same 404 as an unknown one.
            BookDto? book = await catalog.GetBookAsync(bookId, token);
            if (book is null || book.IsDeleted)
                throw new DomainException(CatalogErrors.BookNotFound);

            await applicationLock.AcquireAsync(BorrowingLocks.Staff(staffMemberId), token);
            await applicationLock.AcquireAsync(BorrowingLocks.Book(bookId), token);

            // Restricted: may borrow one book; reserving follows the setting (BR-MEM-3, BQ-G2-05, BQ-BA-10).
            StaffMemberStatus status = await staffMembers.GetStatusAsync(staffMemberId, token);
            if (status == StaffMemberStatus.Restricted && !settings.RestrictedMayReserve)
                throw new DomainException(BorrowingErrors.RestrictedReservationForbidden);

            // Suspended: Staff's reason unless the setting allows reserving (BQ-BA-11); Deactivated: always Staff's reason.
            if (status != StaffMemberStatus.Suspended || !settings.SuspendedMayReserve)
            {
                EligibilityResult eligibility = await staffMembers.CanBorrowAsync(staffMemberId, token);
                if (!eligibility.IsAllowed)
                    throw new DomainException(eligibility.Error!);
            }

            // BR-BOR-3: a reservation only when no copy is Available.
            if (await catalog.GetAvailableCopyAsync(bookId, token) is not null)
                throw new DomainException(BorrowingErrors.ReservationCopyAvailable);

            // BR-BOR-2: not a book the employee already holds.
            IReadOnlyList<Guid> openLoanCopyIds = await loans.GetOpenLoanCopyIdsAsync(staffMemberId, token);
            if (openLoanCopyIds.Count > 0)
            {
                IReadOnlyList<BookCopyDto> heldCopies = await catalog.GetCopiesAsync(openLoanCopyIds, token);
                if (heldCopies.Any(copy => copy.BookId == bookId))
                    throw new DomainException(BorrowingErrors.BookAlreadyOnLoan);
            }

            // The most precise reason first: already in this queue (409), then the limit of active reservations (BR-BOR-2).
            if (await reservations.HasActiveForBookAsync(staffMemberId, bookId, token))
                throw new DomainException(BorrowingErrors.DuplicateActiveReservation);

            if (await reservations.CountActiveAsync(staffMemberId, token) >= settings.MaxActive)
                throw new DomainException(BorrowingErrors.ReservationLimitReached(settings.MaxActive));

            // FIFO: the end of the queue of Waiting reservations.
            int queuePosition = await reservations.CountWaitingForBookAsync(bookId, token) + 1;
            Reservation created = Reservation.Create(idGenerator.NewId(), bookId, staffMemberId, clock.UtcNow, queuePosition);

            await reservations.AddAsync(created, token);
            return created;
        }, ct);

        return ToDto(reservation);
    }

    public async Task<ReservationDto> GetAsync(Guid reservationId, CancellationToken ct = default)
    {
        Reservation? reservation = await reservations.FindAsync(reservationId, ct);

        // NFR-03: a Staff Member sees only their own reservations; another one is reported as not found.
        if (reservation is null || (IsStaffMemberOnly && reservation.StaffMemberId != currentUser.StaffMemberId))
            throw new DomainException(BorrowingErrors.ReservationNotFound);

        return ToDto(reservation);
    }

    public async Task<ReservationDto> CancelAsync(Guid reservationId, CancellationToken ct = default)
    {
        Reservation cancelled = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Resolve the book and enforce NFR-03 before waiting on its queue lock.
            Reservation initial = await reservations.FindAsync(reservationId, token)
                ?? throw new DomainException(BorrowingErrors.ReservationNotFound);

            EnsureCanManage(initial);

            await applicationLock.AcquireAsync(BorrowingLocks.Book(initial.BookId), token);

            // Reload after the lock: another request may have cancelled or transferred this reservation while we waited.
            Reservation reservation = await reservations.GetForUpdateAsync(reservationId, token)
                ?? throw new DomainException(BorrowingErrors.ReservationNotFound);

            EnsureCanManage(reservation);

            int removedQueuePosition = reservation.QueuePosition;
            Guid? heldCopyId = null;
            if (reservation.Status == ReservationStatus.ReadyForPickup)
                heldCopyId = reservation.CopyId
                    ?? throw new InvalidOperationException("A ready reservation must have a held copy.");

            reservation.Cancel();
            // Make the state transition visible to the subsequent queue query, which filters on persisted Waiting rows.
            // This also frees the filtered unique index before a ReadyForPickup copy is assigned to its next holder.
            await reservations.FlushAsync(token);

            if (heldCopyId is { } copyId)
            {
                bool offered = await reservationQueue.TryOfferCopyUnderBookLockAsync(
                    copyId,
                    reservation.BookId,
                    BookCopyStatus.Reserved,
                    token);

                if (!offered)
                    await catalog.SetCopyStatusAsync(copyId, BookCopyStatus.Available, token);
            }
            else
            {
                IReadOnlyList<Reservation> waiting = await reservations.GetWaitingForBookAsync(
                    reservation.BookId,
                    token);

                int nextPosition = 1;
                foreach (Reservation waitingReservation in waiting)
                {
                    // Decrement the positions after the removed row, then normalize the whole queue so it remains 1..n.
                    int positionAfterRemoval = waitingReservation.QueuePosition > removedQueuePosition
                        ? waitingReservation.QueuePosition - 1
                        : waitingReservation.QueuePosition;
                    int normalizedPosition = Math.Min(positionAfterRemoval, nextPosition);

                    if (waitingReservation.QueuePosition != normalizedPosition)
                        waitingReservation.SetQueuePosition(normalizedPosition);

                    nextPosition++;
                }
            }

            return reservation;
        }, ct);

        return ToDto(cancelled);
    }

    private void EnsureCanManage(Reservation reservation)
    {
        if (IsStaffMemberOnly &&
            (currentUser.StaffMemberId is not { } staffMemberId || reservation.StaffMemberId != staffMemberId))
        {
            throw new DomainException(BorrowingErrors.ReservationNotFound);
        }
    }

    private bool IsStaffMemberOnly =>
        !currentUser.IsInRole(StaffRoles.Librarian) && !currentUser.IsInRole(StaffRoles.LibraryAdministrator);

    /// <summary>Who the reservation is for: a Staff Member — themselves only; a Librarian or Administrator — the named one.</summary>
    private Guid ResolveStaffMember(Guid? requested)
    {
        if (IsStaffMemberOnly)
        {
            Guid own = currentUser.StaffMemberId ?? throw new DomainException(BorrowingErrors.ReservationForAnotherStaffMember);
            if (requested is { } other && other != own)
                throw new DomainException(BorrowingErrors.ReservationForAnotherStaffMember);
            return own;
        }

        return requested ?? throw new DomainException(BorrowingErrors.ReservationStaffMemberRequired);
    }

    private static ReservationDto ToDto(Reservation reservation) => new(
        reservation.Id,
        reservation.BookId,
        reservation.StaffMemberId,
        reservation.CreatedAtUtc,
        reservation.Status,
        reservation.QueuePosition,
        reservation.CopyId,
        reservation.ReadyUntilUtc);
}
