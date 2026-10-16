using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
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

        PenaltyOptions settings = options.Value.Penalties;

        // Start suspension only when this operation crosses the threshold.
        bool startsSuspension =
            previousActivePoints < settings.SuspendedFromPoints
            && finalActivePoints >= settings.SuspendedFromPoints;

        StaffMemberStatus nextStatus;
        DateTime? effectiveUntilUtc = null;

        if (startsSuspension)
        {
            nextStatus = StaffMemberStatus.Suspended;
            effectiveUntilUtc =
                nowUtc.AddDays(settings.SuspensionDays);
        }
        else
        {
            nextStatus =
                finalActivePoints >= settings.RestrictedFromPoints
                    ? StaffMemberStatus.Restricted
                    : StaffMemberStatus.Active;
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