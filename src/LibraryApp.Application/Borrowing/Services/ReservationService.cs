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

/// <summary>Creates reservations while enforcing BOR-07 within one serialized database transaction.</summary>
public sealed class ReservationService(
    IStaffMemberService staffMembers,
    ICatalogService catalog,
    ILoanRepository loans,
    IReservationRepository reservations,
    IUnitOfWork unitOfWork,
    IApplicationLock applicationLock,
    IClock clock,
    IIdGenerator idGenerator) : IReservationService
{
    private const int MaximumActiveReservations = 3;

    public async Task<ReservationDto> CreateAsync(
        Guid bookId,
        Guid staffMemberId,
        CancellationToken ct = default)
    {
        if (bookId == Guid.Empty || staffMemberId == Guid.Empty)
            throw new DomainException(BorrowingErrors.InvalidReferenceId);

        Reservation reservation = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Keep the shared lock order (staff, then book) to protect both the employee limit and book queue.
            await applicationLock.AcquireAsync($"borrowing:reservation:staff:{staffMemberId:N}", token);
            await applicationLock.AcquireAsync($"borrowing:reservation:book:{bookId:N}", token);

            BookDto book = await catalog.GetBookAsync(bookId, token)
                ?? throw new DomainException(CatalogErrors.BookNotFound);
            if (book.IsDeleted)
                throw new DomainException(CatalogErrors.BookDeleted);

            // Group 2 allows Restricted members to borrow, but BOR-07 explicitly excludes them from reservations.
            StaffMemberStatus status = await staffMembers.GetStatusAsync(staffMemberId, token);
            if (status == StaffMemberStatus.Restricted)
                throw new DomainException(BorrowingErrors.RestrictedReservationForbidden);

            EligibilityResult eligibility = await staffMembers.CanBorrowAsync(staffMemberId, token);
            if (!eligibility.IsAllowed)
                throw new DomainException(eligibility.Error!);

            if (await catalog.GetAvailableCopyAsync(bookId, token) is not null)
                throw new DomainException(BorrowingErrors.ReservationCopyAvailable);

            if (await reservations.CountActiveAsync(staffMemberId, token) >= MaximumActiveReservations)
                throw new DomainException(BorrowingErrors.ReservationLimitReached);

            if (await reservations.HasActiveForBookAsync(staffMemberId, bookId, token))
                throw new DomainException(BorrowingErrors.DuplicateActiveReservation);

            IReadOnlyList<Guid> openLoanCopyIds =
                await loans.GetOpenLoanCopyIdsAsync(staffMemberId, token);
            if (openLoanCopyIds.Count > 0)
            {
                IReadOnlyList<BookCopyDto> openLoanCopies = await catalog.GetCopiesAsync(openLoanCopyIds, token);
                if (openLoanCopies.Any(copy => copy.BookId == bookId))
                    throw new DomainException(BorrowingErrors.BookAlreadyOnLoan);
            }

            int queuePosition = checked(await reservations.CountWaitingForBookAsync(bookId, token) + 1);
            Reservation created = Reservation.Create(
                idGenerator.NewId(),
                bookId,
                staffMemberId,
                clock.UtcNow,
                queuePosition);

            await reservations.AddAsync(created, token);
            return created;
        }, ct);

        return ToDto(reservation);
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
