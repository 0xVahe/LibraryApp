using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Jobs;

/// <summary>
/// Decides whether this instance may run a daily job now (SD-10) and records how the run ended. Each method is its own
/// short transaction; the work of the job runs between them in the job's own transactions.
/// </summary>
public sealed class JobRunStore(LibraryDbContext db, IUnitOfWork unitOfWork, IApplicationLock locks, IIdGenerator ids, IClock clock)
{
    /// <summary>
    /// The run to do for <paramref name="localDate"/>, or null when there is nothing to do: the day already succeeded,
    /// another instance is running it, the last failure is too recent, or the attempts of the day are used up.
    /// </summary>
    public Task<JobRun?> TryStartAsync(string jobName, DateOnly localDate, JobsOptions options, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Two instances asking at the same moment: the second waits here, then sees the first one's row.
            await locks.AcquireAsync($"job:{jobName}", token);

            DateTime now = clock.UtcNow;
            JobRun? run = await db.JobRuns.SingleOrDefaultAsync(r => r.JobName == jobName && r.LocalDate == localDate, token);
            if (run is null)
            {
                run = JobRun.Start(ids.NewId(), jobName, localDate, now);
                db.JobRuns.Add(run);
                return run;
            }

            bool mayRestart = run.Status switch
            {
                JobRunStatus.Succeeded => false,
                JobRunStatus.Running => run.StartedAtUtc + options.Lease <= now,   // its instance stopped mid-run
                _ => run.FinishedAtUtc + options.RetryDelay <= now,
            };
            if (!mayRestart || run.Attempts >= options.MaxAttemptsPerDay)
                return null;

            run.Restart(now);
            return run;
        }, ct);

    public Task SucceedAsync(Guid runId, string summary, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
            (await db.JobRuns.SingleAsync(r => r.Id == runId, token)).Succeed(clock.UtcNow, summary), ct);

    public Task FailAsync(Guid runId, string error, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
            (await db.JobRuns.SingleAsync(r => r.Id == runId, token)).Fail(clock.UtcNow, error), ct);

    /// <summary>When the last successful run of the job started; null when it never succeeded.</summary>
    public Task<DateTime?> LastSuccessfulStartAsync(string jobName, CancellationToken ct) =>
        db.JobRuns.AsNoTracking()
            .Where(r => r.JobName == jobName && r.Status == JobRunStatus.Succeeded)
            .MaxAsync(r => (DateTime?)r.StartedAtUtc, ct);
}
