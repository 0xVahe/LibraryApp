using LibraryApp.Domain.Common;

namespace LibraryApp.Infrastructure.Jobs;

/// <summary>
/// One run of a daily job for one local date (SD-10). The unique (JobName, LocalDate) row is what lets only one API
/// instance run a job per day; it also records when the run happened, how it ended and what it did.
/// </summary>
public sealed class JobRun : Entity
{
    public const int JobNameMaxLength = 100;
    public const int SummaryMaxLength = 1000;
    public const int ErrorMaxLength = 2000;

    private JobRun() { }

    private JobRun(Guid id, string jobName, DateOnly localDate, DateTime startedAtUtc, DateTime leaseUntilUtc) : base(id)
    {
        JobName = jobName;
        LocalDate = localDate;
        Status = JobRunStatus.Running;
        Attempts = 1;
        StartedAtUtc = startedAtUtc;
        LeaseUntilUtc = leaseUntilUtc;
    }

    public string JobName { get; private set; } = "";

    /// <summary>The Asia/Yerevan date the run is for.</summary>
    public DateOnly LocalDate { get; private set; }

    public JobRunStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    /// <summary>
    /// Until when the instance running the attempt owns it. The runner extends it while the job works; a Running record
    /// past this moment belongs to an instance that stopped, and another instance takes the run over.
    /// </summary>
    public DateTime LeaseUntilUtc { get; private set; }

    public DateTime? FinishedAtUtc { get; private set; }

    public string? Summary { get; private set; }

    public string? LastError { get; private set; }

    public static JobRun Start(Guid id, string jobName, DateOnly localDate, DateTime startedAtUtc, DateTime leaseUntilUtc) =>
        new(id, jobName, localDate, startedAtUtc, leaseUntilUtc);

    /// <summary>A failed run, or a run whose instance stopped mid-run, starts again as the next attempt.</summary>
    public void Restart(DateTime startedAtUtc, DateTime leaseUntilUtc)
    {
        Status = JobRunStatus.Running;
        Attempts++;
        StartedAtUtc = startedAtUtc;
        LeaseUntilUtc = leaseUntilUtc;
        FinishedAtUtc = null;
    }

    /// <summary>The instance running the attempt is still working on it.</summary>
    public void ExtendLease(DateTime leaseUntilUtc) => LeaseUntilUtc = leaseUntilUtc;

    public void Succeed(DateTime finishedAtUtc, string summary)
    {
        Status = JobRunStatus.Succeeded;
        FinishedAtUtc = finishedAtUtc;
        Summary = Cut(summary, SummaryMaxLength);
        LastError = null;
    }

    public void Fail(DateTime finishedAtUtc, string error)
    {
        Status = JobRunStatus.Failed;
        FinishedAtUtc = finishedAtUtc;
        LastError = Cut(error, ErrorMaxLength);
    }

    private static string Cut(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength];
}

/// <summary>Stored as int (SD-11); values never renumbered.</summary>
public enum JobRunStatus
{
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}
