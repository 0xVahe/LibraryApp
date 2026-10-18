namespace LibraryApp.Application.Abstractions.Common;

/// <summary>
/// The work of a job that runs once per local Asia/Yerevan day (SD-10), for example the Borrowing daily job (BOR-06) or
/// the 09:00 reminders of Notifications (NOT-05). The platform runner decides when: at the time set in
/// <c>Jobs:Schedules:&lt;name&gt;</c>, once per day across all API instances, catching up after downtime and retrying a
/// failed run. The job only does the work and decides its own transactions (one <see cref="IUnitOfWork"/> per batch).
/// Register it in the module with <c>services.AddDailyJob&lt;TJob&gt;(name)</c>.
/// </summary>
public interface IDailyJob
{
    /// <summary>Does the work of one run; returns a one-line summary for the log and the run record.</summary>
    Task<string> RunAsync(DailyJobRun run, CancellationToken ct);
}

/// <param name="JobName">The registered name of the job.</param>
/// <param name="LocalDate">The Asia/Yerevan date this run is for.</param>
/// <param name="LastSuccessfulStartUtc">
/// When the last successful run started; null on the first run ever. A job that must not miss days after downtime
/// processes everything due since then, not only today.
/// </param>
public sealed record DailyJobRun(string JobName, DateOnly LocalDate, DateTime? LastSuccessfulStartUtc);
