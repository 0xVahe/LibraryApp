using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Jobs;

/// <summary>
/// Decides whether this instance may run a daily job now (SD-10) and records how the run ended. Each method is its own
/// short transaction under the lock <c>job:{name}</c>; the work of the job runs between them in the job's own transactions.
/// </summary>
public sealed class JobRunStore(LibraryDbContext db, IUnitOfWork unitOfWork, IApplicationLock locks, IIdGenerator ids, IClock clock)
{
    /// <summary>
    /// The attempt to run for <paramref name="localDate"/>, or null when there is nothing to do: the day already succeeded,
    /// another instance holds the lease, the last failure is too recent, or the attempts of the day are used up.
    /// </summary>
    public Task<JobRunClaim?> TryStartAsync(string jobName, DateOnly localDate, JobsOptions options, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Two instances asking at the same moment: the second waits here, then sees the first one's row.
            await locks.AcquireAsync(LockName(jobName), token);

            DateTime now = clock.UtcNow;
            JobRun? run = await db.JobRuns.SingleOrDefaultAsync(r => r.JobName == jobName && r.LocalDate == localDate, token);
            if (run is null)
            {
                run = JobRun.Start(ids.NewId(), jobName, localDate, now, now + options.Lease);
                db.JobRuns.Add(run);
                return Claim(run);
            }

            bool mayRestart = run.Status switch
            {
                JobRunStatus.Succeeded => false,
                JobRunStatus.Running => run.LeaseUntilUtc <= now,   // its instance stopped mid-run
                _ => run.FinishedAtUtc + options.RetryDelay <= now,
            };
            if (!mayRestart || run.Attempts >= options.MaxAttemptsPerDay)
                return null;

            run.Restart(now, now + options.Lease);
            return Claim(run);
        }, ct);

    /// <summary>Extends the lease of a running attempt. False when the attempt is no longer the current one.</summary>
    public Task<bool> ExtendLeaseAsync(JobRunClaim claim, TimeSpan lease, CancellationToken ct) =>
        UpdateCurrentAttemptAsync(claim, (run, now) => run.ExtendLease(now + lease), ct);

    /// <summary>Records a successful attempt. False when another instance has taken the run over since.</summary>
    public Task<bool> SucceedAsync(JobRunClaim claim, string summary, CancellationToken ct) =>
        UpdateCurrentAttemptAsync(claim, (run, now) => run.Succeed(now, summary), ct);

    /// <summary>Records a failed attempt. False when another instance has taken the run over since.</summary>
    public Task<bool> FailAsync(JobRunClaim claim, string error, CancellationToken ct) =>
        UpdateCurrentAttemptAsync(claim, (run, now) => run.Fail(now, error), ct);

    /// <summary>When the last successful run of the job started; null when it never succeeded.</summary>
    public Task<DateTime?> LastSuccessfulStartAsync(string jobName, CancellationToken ct) =>
        db.JobRuns.AsNoTracking()
            .Where(r => r.JobName == jobName && r.Status == JobRunStatus.Succeeded)
            .MaxAsync(r => (DateTime?)r.StartedAtUtc, ct);

    /// <summary>
    /// Changes the run only while it is still the attempt this instance claimed: an instance that lost its lease never
    /// overwrites the attempt that took the run over. The lock orders the change after or before a takeover, never between.
    /// </summary>
    private Task<bool> UpdateCurrentAttemptAsync(JobRunClaim claim, Action<JobRun, DateTime> change, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await locks.AcquireAsync(LockName(claim.JobName), token);

            JobRun? run = await db.JobRuns.SingleOrDefaultAsync(
                r => r.Id == claim.RunId && r.Status == JobRunStatus.Running && r.Attempts == claim.Attempt, token);
            if (run is null)
                return false;

            change(run, clock.UtcNow);
            return true;
        }, ct);

    private static string LockName(string jobName) => $"job:{jobName}";

    private static JobRunClaim Claim(JobRun run) => new(run.Id, run.JobName, run.LocalDate, run.Attempts);
}

/// <summary>One attempt of a run that this instance claimed: only this attempt may extend the lease or record the result.</summary>
public sealed record JobRunClaim(Guid RunId, string JobName, DateOnly LocalDate, int Attempt);
