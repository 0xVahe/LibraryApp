using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Borrowing.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Jobs.Borrowing;

/// <summary>
/// Daily Borrowing maintenance:
/// marks overdue loans, expires held reservations and penalties, recalculates staff standing, and cancels reservations
/// that can never be served: those of a deleted book and those of a deactivated member.
/// Scheduling and run tracking belong to the shared daily-job runner.
/// </summary>
public sealed class BorrowingDailyJob(
    IServiceScopeFactory scopes,
    IClock clock,
    IOptions<BorrowingOptions> options,
    ILogger<BorrowingDailyJob> logger) : IDailyJob
{
    public const string Name = "Borrowing";

    public async Task<string> RunAsync(
        DailyJobRun run,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        DateTime selectionTimeUtc = clock.UtcNow;

        DateTime cutoffUtc = options.Value.Loans.OverdueFrom switch
        {
            OverdueFromMode.NextLocalDay =>
                clock.StartOfLocalDayUtc(
                    clock.ToLocalDate(selectionTimeUtc)),

            OverdueFromMode.DueTime =>
                selectionTimeUtc,

            _ => throw new InvalidOperationException(
                "Unknown overdue calculation mode.")
        };

        IReadOnlyList<Guid> loanIds;
        IReadOnlyList<Guid> reservationIds;
        IReadOnlyList<Guid> staffIds;
        IReadOnlyList<Guid> queuedBookIds;
        IReadOnlyList<Guid> reservingStaffIds;

        // Candidate selection gets its own short-lived scope.
        // No tracked entities are carried into the processing scopes.
        await using (var scope = scopes.CreateAsyncScope())
        {
            var query = scope.ServiceProvider
                .GetRequiredService<IBorrowingDailyJobQuery>();

            loanIds = await query.GetActiveLoanIdsDueBeforeAsync(
                cutoffUtc,
                ct);

            reservationIds = await query.GetReadyReservationIdsPastDeadlineAsync(
                selectionTimeUtc,
                ct);

            staffIds = await query.GetStaffIdsWithActivePenaltiesAsync(
                ct);

            queuedBookIds = await query.GetBookIdsWithWaitingReservationsAsync(
                ct);

            reservingStaffIds = await query.GetStaffIdsWithActiveReservationsAsync(
                ct);
        }

        logger.LogInformation(
            "Borrowing maintenance for {LocalDate}: "
            + "{LoanCandidates} loan candidates and "
            + "{PickupCandidates} pickup candidates and "
            + "{StaffCandidates} staff candidates",
            run.LocalDate,
            loanIds.Count,
            reservationIds.Count,
            staffIds.Count);

        int overdueLoans = 0;
        int expiredPickups = 0;
        int expiredPenalties = 0;
        int changedStatuses = 0;
        int cancelledReservations = 0;
        int failures = 0;

        foreach (Guid loanId in loanIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // A fresh scope means a fresh scoped DbContext.
                await using var scope = scopes.CreateAsyncScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<IBorrowingDailyJobService>();

                bool changed = await service.MarkLoanOverdueAsync(
                    loanId,
                    ct);

                if (changed)
                {
                    overdueLoans++;
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;

                logger.LogError(
                    ex,
                    "Borrowing maintenance failed for loan {LoanId}",
                    loanId);
            }
        }

        foreach (Guid staffMemberId in staffIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await using var scope = scopes.CreateAsyncScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<IBorrowingDailyJobService>();

                PenaltyRecalculationResult result =
                    await service.RecalculateStaffAsync(
                        staffMemberId,
                        ct);

                expiredPenalties += result.ExpiredRecords;

                if (result.StatusChanged)
                {
                    changedStatuses++;
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;

                logger.LogError(
                    ex,
                    "Borrowing maintenance failed for staff member "
                    + "{StaffMemberId}",
                    staffMemberId);
            }
        }

        foreach (Guid reservationId in reservationIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await using var scope = scopes.CreateAsyncScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<IBorrowingDailyJobService>();

                bool changed = await service.ExpirePickupAsync(
                    reservationId,
                    ct);

                if (changed)
                {
                    expiredPickups++;
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;

                logger.LogError(
                    ex,
                    "Borrowing maintenance failed for reservation {ReservationId}",
                    reservationId);
            }
        }

        // After the pickup expiry: a book deleted in Catalog can never serve its queue.
        foreach (Guid bookId in queuedBookIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await using var scope = scopes.CreateAsyncScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<IBorrowingDailyJobService>();

                int cancelled = await service.CancelWaitingReservationsOfDeletedBookAsync(
                    bookId,
                    ct);

                if (cancelled > 0)
                {
                    cancelledReservations += cancelled;

                    logger.LogInformation(
                        "Cancelled {Count} waiting reservations of deleted book {BookId}",
                        cancelled,
                        bookId);
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;

                logger.LogError(
                    ex,
                    "Borrowing maintenance failed for the queue of book {BookId}",
                    bookId);
            }
        }

        // A deactivated member never collects a copy; their place and any held copy go to the others.
        foreach (Guid staffMemberId in reservingStaffIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await using var scope = scopes.CreateAsyncScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<IBorrowingDailyJobService>();

                int cancelled = await service.CancelReservationsOfDeactivatedMemberAsync(
                    staffMemberId,
                    ct);

                if (cancelled > 0)
                {
                    cancelledReservations += cancelled;

                    logger.LogInformation(
                        "Cancelled {Count} reservations of deactivated staff member {StaffMemberId}",
                        cancelled,
                        staffMemberId);
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;

                logger.LogError(
                    ex,
                    "Borrowing maintenance failed for the reservations of staff member {StaffMemberId}",
                    staffMemberId);
            }
        }

        ct.ThrowIfCancellationRequested();

        string summary =
            $"Overdue loans: {overdueLoans}; "
            + $"expired pickups: {expiredPickups}; "
            + $"expired penalties: {expiredPenalties}; "
            + $"changed statuses: {changedStatuses}; "
            + $"cancelled reservations: {cancelledReservations}; "
            + $"failures: {failures}.";

        if (failures > 0)
        {
            // Every candidate has been attempted.
            // Report failure so the shared runner can retry.
            // Previously committed items remain committed.
            throw new InvalidOperationException(
                $"Borrowing maintenance completed with failures. {summary}");
        }

        return summary;
    }
}
