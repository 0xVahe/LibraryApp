using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>Finding and waiving penalty records (BOR-08); Librarian and Library Administrator only.</summary>
public interface IPenaltyAdministrationService
{
    /// <summary>One page of a staff member's penalty records, newest first; <paramref name="status"/> narrows the list.</summary>
    Task<PagedResult<PenaltyDto>> ListAsync(
        Guid staffMemberId,
        PenaltyRecordStatus? status,
        PageRequest page,
        CancellationToken ct = default);

    /// <summary>
    /// Waives an Active record with a mandatory reason (BOR-08) and recalculates the staff member's status in the same
    /// transaction, so the status is right at once (the daily job never looks at a member without Active records).
    /// </summary>
    Task<PenaltyDto> WaiveAsync(Guid penaltyId, WaivePenaltyRequest request, CancellationToken ct = default);
}

/// <summary>
/// The waiver runs under the staff lock of the record's owner (the lock of every penalty operation) and uses the
/// shared penalty component for the status (<see cref="IPenaltyService.RecalculateStatusAsync"/>): below
/// <c>RestrictedFromPoints</c> the member is Active, <c>StaffStatusChanged</c> is published only on a real change.
/// </summary>
public sealed class PenaltyAdministrationService(
    IPenaltyRepository penalties,
    IPenaltyService penaltyComponent,
    IUnitOfWork unitOfWork,
    IApplicationLock locks,
    IClock clock) : IPenaltyAdministrationService
{
    public async Task<PagedResult<PenaltyDto>> ListAsync(
        Guid staffMemberId,
        PenaltyRecordStatus? status,
        PageRequest page,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        PagedResult<PenaltyRecord> records = await penalties.PageForStaffAsync(staffMemberId, status, page, ct);
        return new PagedResult<PenaltyDto>(
            records.Items.Select(ToDto).ToList(), records.TotalCount, records.Page, records.PageSize);
    }

    public async Task<PenaltyDto> WaiveAsync(Guid penaltyId, WaivePenaltyRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PenaltyRecord waived = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Read only, to find whose lock is needed.
            PenaltyRecord initial = await penalties.FindAsync(penaltyId, token)
                ?? throw new DomainException(BorrowingErrors.PenaltyNotFound);

            await locks.AcquireAsync(BorrowingLocks.Staff(initial.StaffMemberId), token);

            // Read again under the lock: a parallel waiver or the daily job's expiry may have closed the record.
            PenaltyRecord record = await penalties.GetForUpdateAsync(penaltyId, token)
                ?? throw new DomainException(BorrowingErrors.PenaltyNotFound);

            // Only an Active record (422 borrowing.penalty_not_active); the reason is checked again by the entity.
            record.Waive(request.Reason);

            // The tracked record is already Waived, so it no longer counts in the recalculation.
            await penaltyComponent.RecalculateStatusAsync(record.StaffMemberId, clock.UtcNow, token);

            return record;
        }, ct);

        return ToDto(waived);
    }

    private static PenaltyDto ToDto(PenaltyRecord record) => new(
        record.Id,
        record.StaffMemberId,
        record.LoanId,
        record.Points,
        record.Reason,
        record.Status,
        record.CreatedAtUtc,
        record.ExpiresAtUtc,
        record.WaivedReason);
}
