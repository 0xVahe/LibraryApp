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

    private JobRun(Guid id, string jobName, DateOnly localDate, DateTime startedAtUtc) : base(id)
    {
        JobName = jobName;
        LocalDate = localDate;
        Status = JobRunStatus.Running;
        Attempts = 1;
        StartedAtUtc = startedAtUtc;
    }

    public string JobName { get; private set; } = "";

    /// <summary>The Asia/Yerevan date the run is for.</summary>
    public DateOnly LocalDate { get; private set; }

    public JobRunStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime? FinishedAtUtc { get; private set; }

    public string? Summary { get; private set; }

    public string? LastError { get; private set; }

    public static JobRun Start(Guid id, string jobName, DateOnly localDate, DateTime startedAtUtc) =>
        new(id, jobName, localDate, startedAtUtc);

    /// <summary>A failed run, or a run whose instance stopped mid-run, starts again.</summary>
    public void Restart(DateTime startedAtUtc)
    {
        Status = JobRunStatus.Running;
        Attempts++;
        StartedAtUtc = startedAtUtc;
        FinishedAtUtc = null;
    }

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
