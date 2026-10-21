using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// Applies penalties and updates staff standing inside the caller's transaction.
/// The caller must hold the staff member's application lock.
/// </summary>
public sealed class PenaltyService(
    IPenaltyRepository penalties,
    PenaltyPolicy policy,
    IIdGenerator ids,
    IEventBus events,
    IStaffMemberService staff,
    IOptions<BorrowingOptions> options) : IPenaltyService
{
    /// <inheritdoc />
    public async Task<PenaltyRecalculationResult> RecalculateStatusAsync(
        Guid staffMemberId,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        if (staffMemberId == Guid.Empty)
            throw new DomainException(BorrowingErrors.InvalidReferenceId);

        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new DomainException(BorrowingErrors.TimestampMustBeUtc);

        ct.ThrowIfCancellationRequested();

        IReadOnlyList<PenaltyRecord> records = await penalties.GetForStaffAsync(staffMemberId, ct);

        // BOR-06: records past their 90 days stop counting and are marked Expired.
        int expiredRecords = 0;
        foreach (PenaltyRecord record in records)
        {
            if (record.Status == PenaltyRecordStatus.Active && record.ExpiresAtUtc <= nowUtc)
            {
                record.Expire(nowUtc);
                expiredRecords++;
            }
        }

        int activePoints = records.Where(record => record.CountsAt(nowUtc)).Sum(record => record.Points);

        StaffMemberStatus currentStatus = await staff.GetStatusAsync(staffMemberId, ct);

        // Borrowing never reactivates a member deactivated by Staff.
        if (currentStatus == StaffMemberStatus.Deactivated)
            return new PenaltyRecalculationResult(expiredRecords, activePoints, StatusChanged: false);

        StaffMemberStatus nextStatus = NextStatus(currentStatus, activePoints, SuspensionEndsAtUtc(records, nowUtc), nowUtc);

        if (nextStatus == currentStatus)
            return new PenaltyRecalculationResult(expiredRecords, activePoints, StatusChanged: false);

        string reason = $"Penalty standing recalculated: {activePoints} active points.";

        await staff.SetStatusAsync(staffMemberId, nextStatus, reason, ct);

        await events.PublishAsync(
            new StaffStatusChanged(
                ids.NewId(),
                nowUtc,
                staffMemberId,
                currentStatus,
                nextStatus,
                reason,
                EffectiveUntilUtc: null),
            ct);

        return new PenaltyRecalculationResult(expiredRecords, activePoints, StatusChanged: true);
    }

    /// <summary>
    /// The penalty-point policy table: below the Restricted threshold the member is Active; a suspension
    /// lasts until its end; after it, and whenever points are at or above the Restricted threshold, the member is
    /// Restricted. A suspension is never started here: only a return that crosses the threshold starts one
    /// (<see cref="ApplyAsync"/>).
    /// </summary>
    private StaffMemberStatus NextStatus(
        StaffMemberStatus currentStatus,
        int activePoints,
        DateTime? suspensionEndsAtUtc,
        DateTime nowUtc)
    {
        if (policy.GetStatus(activePoints) == StaffMemberStatus.Active)
            return StaffMemberStatus.Active;

        if (currentStatus == StaffMemberStatus.Suspended && suspensionEndsAtUtc is { } end && nowUtc < end)
            return StaffMemberStatus.Suspended;

        return StaffMemberStatus.Restricted;
    }

    /// <summary>
    /// End of the latest suspension, computed from the records (no stored field): the suspension starts at the
    /// creation time of the record with which the counting points first reached the Suspended threshold, and lasts
    /// <c>Borrowing:Penalties:SuspensionDays</c>. Waived records never count, so waiving the record that made the
    /// threshold moves the start (by design). Null when the points never reached the threshold.
    /// </summary>
    private DateTime? SuspensionEndsAtUtc(IReadOnlyList<PenaltyRecord> records, DateTime nowUtc)
    {
        List<PenaltyRecord> counted = records.Where(record => record.Status != PenaltyRecordStatus.Waived).ToList();

        // Points that counted at a moment of the past: created by then and not yet expired then.
        int PointsAt(DateTime moment, bool includeMoment) => counted
            .Where(record => (includeMoment ? record.CreatedAtUtc <= moment : record.CreatedAtUtc < moment)
                             && moment < record.ExpiresAtUtc)
            .Sum(record => record.Points);

        // Records of one return share one timestamp: each moment is one operation.
        DateTime? start = counted
            .Select(record => record.CreatedAtUtc)
            .Where(moment => moment <= nowUtc)
            .Distinct()
            .Where(moment => policy.GetStatus(PointsAt(moment, includeMoment: false)) != StaffMemberStatus.Suspended
                             && policy.GetStatus(PointsAt(moment, includeMoment: true)) == StaffMemberStatus.Suspended)
            .Select(moment => (DateTime?)moment)
            .Max();

        return start?.AddDays(options.Value.Penalties.SuspensionDays);
    }

    public async Task<PenaltyApplicationResult> ApplyAsync(
        Loan loan,
        IReadOnlyList<ReturnPenaltyItem> items,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(loan);
        ArgumentNullException.ThrowIfNull(items);

        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "The penalty time must be UTC.",
                nameof(nowUtc));
        }

        ct.ThrowIfCancellationRequested();

        // Load existing records before adding this operation's penalties.
        IReadOnlyList<PenaltyRecord> existing =
            await penalties.GetForStaffAsync(loan.StaffMemberId, ct);

        int previousActivePoints = existing
            .Where(penalty => penalty.CountsAt(nowUtc))
            .Sum(penalty => penalty.Points);

        // Construct and validate every new record before staging changes.
        var created = new List<PenaltyRecord>(items.Count);

        foreach (ReturnPenaltyItem item in items)
        {
            PenaltyRecord record = PenaltyRecord.Create(
                ids.NewId(),
                loan.StaffMemberId,
                loan.Id,
                item.Points,
                item.Reason,
                nowUtc,
                policy.ExpiresAtUtc(nowUtc));

            created.Add(record);
        }

        int pointsApplied = created.Sum(penalty => penalty.Points);

        // New records are not saved yet, so include them explicitly.
        int finalActivePoints =
            checked(previousActivePoints + pointsApplied);

        foreach (PenaltyRecord record in created)
            await penalties.AddAsync(record, ct);

        // One event per operation, not per record: a late and damaged return is one notice with the sum of its points
        // and the final total (catalogue "points added and new total"); NOT-06 would keep only one notice per loan and day.
        if (pointsApplied > 0)
        {
            await events.PublishAsync(
                new PenaltyApplied(
                    ids.NewId(),
                    nowUtc,
                    loan.StaffMemberId,
                    loan.Id,
                    pointsApplied,
                    finalActivePoints,
                    string.Join("; ", created.Where(record => record.Points > 0).Select(record => record.Reason))),
                ct);
        }

        await UpdateStaffStatusAsync(
            loan.StaffMemberId,
            previousActivePoints,
            finalActivePoints,
            nowUtc,
            ct);

        return new PenaltyApplicationResult(
            pointsApplied,
            finalActivePoints);
    }

    private async Task UpdateStaffStatusAsync(
        Guid staffMemberId,
        int previousActivePoints,
        int finalActivePoints,
        DateTime nowUtc,
        CancellationToken ct)
    {
        StaffMemberStatus currentStatus =
            await staff.GetStatusAsync(staffMemberId, ct);

        // Borrowing cannot reactivate a deactivated employee.
        // Ending an existing suspension belongs to the daily job.
        if (currentStatus is StaffMemberStatus.Deactivated
            or StaffMemberStatus.Suspended)
        {
            return;
        }

        // The thresholds live in PenaltyPolicy only (one policy for return, daily job, lost and waive).
        StaffMemberStatus byPoints = policy.GetStatus(finalActivePoints);

        // A suspension starts only when this operation crosses the threshold: after a suspension has ended the
        // employee stays Restricted while the points stay high, and is not suspended again without a crossing.
        bool startsSuspension =
            policy.GetStatus(previousActivePoints) != StaffMemberStatus.Suspended
            && byPoints == StaffMemberStatus.Suspended;

        StaffMemberStatus nextStatus;
        DateTime? effectiveUntilUtc = null;

        if (startsSuspension)
        {
            nextStatus = StaffMemberStatus.Suspended;
            effectiveUntilUtc =
                nowUtc.AddDays(options.Value.Penalties.SuspensionDays);
        }
        else
        {
            nextStatus = byPoints == StaffMemberStatus.Suspended
                ? StaffMemberStatus.Restricted
                : byPoints;
        }

        if (currentStatus == nextStatus)
            return;

        string reason =
            $"Active penalty points recalculated: {finalActivePoints}.";

        await staff.SetStatusAsync(
            staffMemberId,
            nextStatus,
            reason,
            ct);

        await events.PublishAsync(
            new StaffStatusChanged(
                ids.NewId(),
                nowUtc,
                staffMemberId,
                currentStatus,
                nextStatus,
                reason,
                effectiveUntilUtc),
            ct);
    }
}