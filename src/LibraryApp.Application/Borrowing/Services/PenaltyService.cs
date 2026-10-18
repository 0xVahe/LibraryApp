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
        public async Task<PenaltyRecalculationResult> RecalculateStatusAsync(
        Guid staffMemberId,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        if (staffMemberId == Guid.Empty)
        {
            throw new DomainException(
                BorrowingErrors.InvalidReferenceId);
        }

        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                BorrowingErrors.TimestampMustBeUtc);
        }

        ct.ThrowIfCancellationRequested();

        IReadOnlyList<PenaltyRecord> records =
            await penalties.GetForStaffAsync(staffMemberId, ct);

        int expiredRecords = 0;

        foreach (PenaltyRecord record in records)
        {
            if (record.Status == PenaltyRecordStatus.Active
                && record.ExpiresAtUtc <= nowUtc)
            {
                record.Expire(nowUtc);
                expiredRecords++;
            }
        }

        int activePoints = records
            .Where(record => record.CountsAt(nowUtc))
            .Sum(record => record.Points);

        StaffMemberStatus currentStatus =
            await staff.GetStatusAsync(staffMemberId, ct);

        // Penalties can expire, but Borrowing must never reactivate
        // a member who has been deactivated by Staff.
        if (currentStatus == StaffMemberStatus.Deactivated)
        {
            return new PenaltyRecalculationResult(
                expiredRecords,
                activePoints,
                StatusChanged: false);
        }

        StaffMemberStatus byPoints = policy.GetStatus(activePoints);

        StaffMemberStatus nextStatus;

        // The requirements restore Active when points fall below
        // the Restricted threshold.
        if (byPoints == StaffMemberStatus.Active)
        {
            nextStatus = StaffMemberStatus.Active;
        }
        else if (currentStatus == StaffMemberStatus.Suspended)
        {
            DateTime suspensionUntilUtc =
                GetLatestSuspensionDeadline(records, nowUtc)
                ?? throw new DomainException(
                    BorrowingErrors.SuspensionHistoryMissing);

            if (nowUtc < suspensionUntilUtc)
            {
                nextStatus = StaffMemberStatus.Suspended;
            }
            else
            {
                nextStatus = GetNonSuspendedStatus(
                    activePoints,
                    records,
                    nowUtc);
            }
        }
        else
        {
            // Recalculation does not create a fresh suspension merely
            // because existing points remain above its threshold.
            nextStatus = GetNonSuspendedStatus(
                activePoints,
                records,
                nowUtc);
        }

        if (nextStatus == currentStatus)
        {
            return new PenaltyRecalculationResult(
                expiredRecords,
                activePoints,
                StatusChanged: false);
        }

        string reason =
            $"Penalty standing recalculated: {activePoints} active points.";

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
                EffectiveUntilUtc: null),
            ct);

        return new PenaltyRecalculationResult(
            expiredRecords,
            activePoints,
            StatusChanged: true);
    }
    
    private StaffMemberStatus GetNonSuspendedStatus(
        int activePoints,
        IReadOnlyList<PenaltyRecord> records,
        DateTime nowUtc)
    {
        StaffMemberStatus byPoints = policy.GetStatus(activePoints);

        if (byPoints == StaffMemberStatus.Active)
        {
            return StaffMemberStatus.Active;
        }

        DateTime? suspensionUntilUtc =
            GetLatestSuspensionDeadline(records, nowUtc);

        if (suspensionUntilUtc.HasValue
            && nowUtc >= suspensionUntilUtc.Value)
        {
            return options.Value.Penalties.AfterSuspension switch
            {
                AfterSuspensionMode.RestrictedByPoints =>
                    StaffMemberStatus.Restricted,

                AfterSuspensionMode.Active =>
                    StaffMemberStatus.Active,

                _ => throw new InvalidOperationException(
                    "Unknown after-suspension mode.")
            };
        }

        // Starting a suspension requires a new threshold crossing
        // in ApplyAsync. High existing points alone are insufficient.
        return StaffMemberStatus.Restricted;
    }

    private static DateTime? GetLatestSuspensionDeadline(
        IReadOnlyList<PenaltyRecord> records,
        DateTime nowUtc)
    {
        return records
            .Where(record =>
                record.CreatedAtUtc <= nowUtc
                && record.SuspensionUntilUtc.HasValue)
            .OrderByDescending(record => record.CreatedAtUtc)
            .ThenByDescending(record => record.SuspensionUntilUtc)
            .Select(record => record.SuspensionUntilUtc)
            .FirstOrDefault();
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

        int runningActivePoints = previousActivePoints;

        foreach (PenaltyRecord record in created)
        {
            await penalties.AddAsync(record, ct);

            runningActivePoints =
                checked(runningActivePoints + record.Points);

            if (record.Points == 0)
                continue;

            await events.PublishAsync(
                new PenaltyApplied(
                    ids.NewId(),
                    nowUtc,
                    record.Id,
                    record.StaffMemberId,
                    record.LoanId,
                    record.Points,
                    runningActivePoints,
                    record.Reason),
                ct);
        }

        await UpdateStaffStatusAsync(
            loan.StaffMemberId,
            previousActivePoints,
            finalActivePoints,
            existing,
            created,
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
        IReadOnlyList<PenaltyRecord> existing,
        IReadOnlyList<PenaltyRecord> created,
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
        // employee stays Restricted while the points stay high (BQ-BA-01), and is not suspended again without a crossing.
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

            // All records created by this operation have the same timestamp.
            // Store the deadline on one positive-point record from this operation.
            PenaltyRecord trigger = created.Last(record => record.Points > 0);

            trigger.RecordSuspension(effectiveUntilUtc.Value);
        }
        else
        {
            nextStatus = GetNonSuspendedStatus(
                finalActivePoints,
                existing,
                nowUtc);
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