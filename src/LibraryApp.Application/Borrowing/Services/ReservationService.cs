using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;

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
    IUnitOfWork unitOfWork,
    IApplicationLock applicationLock,
    ICurrentUser currentUser,
    IClock clock,
    IIdGenerator idGenerator) : IReservationService
{
    /// <summary>BR-BOR-2: at most 3 active (Waiting or ReadyForPickup) reservations per employee.</summary>
    public const int MaximumActiveReservations = 3;

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

            // Restricted: may borrow one book but not reserve (BR-MEM-3, BQ-G2-05); Suspended and Deactivated: Staff's reason.
            if (await staffMembers.GetStatusAsync(staffMemberId, token) == StaffMemberStatus.Restricted)
                throw new DomainException(BorrowingErrors.RestrictedReservationForbidden);

            EligibilityResult eligibility = await staffMembers.CanBorrowAsync(staffMemberId, token);
            if (!eligibility.IsAllowed)
                throw new DomainException(eligibility.Error!);

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

            // The most precise reason first: already in this queue (409), then the limit of 3 (BR-BOR-2).
            if (await reservations.HasActiveForBookAsync(staffMemberId, bookId, token))
                throw new DomainException(BorrowingErrors.DuplicateActiveReservation);

            if (await reservations.CountActiveAsync(staffMemberId, token) >= MaximumActiveReservations)
                throw new DomainException(BorrowingErrors.ReservationLimitReached);

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
