using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Jobs;
using LibraryApp.Infrastructure.Jobs.Borrowing;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// The API host without the Borrowing seed (the tests own every loan and penalty record), with the Borrowing schedule
/// due from local midnight so the runner can be started at any time of the day. Jobs stay off in the background
/// (<c>Jobs:Enabled=false</c> from <see cref="ApiHostFactory"/>): the test starts the run itself.
/// </summary>
public sealed class BorrowingDailyJobHost : ApiHostFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Jobs:Schedules:Borrowing:RunAt", "00:00");
    }

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.Remove(services.Single(d => d.ServiceType == typeof(IModuleSeeder) && d.ImplementationType == typeof(BorrowingSeeder)));
}

/// <summary>
/// G3-04 (BOR-06) on SQL Server through the real host: the job is registered with the shared runner, one run changes
/// loans, penalty records and staff statuses with their events in the outbox, and a second run changes nothing.
/// </summary>
public class BorrowingDailyJobDatabaseTests(BorrowingDailyJobHost factory) : IClassFixture<BorrowingDailyJobHost>
{
    [Fact]
    public void The_job_is_registered_with_the_shared_runner_and_its_schedule()
    {
        var registration = Assert.Single(factory.Services.GetServices<DailyJobRegistration>(), r => r.Name == BorrowingDailyJob.Name);
        Assert.Equal(typeof(BorrowingDailyJob), registration.JobType);

        Assert.Single(factory.Services.GetServices<IHostedService>().OfType<DailyJobRunner>(), r => r.JobName == BorrowingDailyJob.Name);

        // The host started, so the runner's validation accepted the schedule (a registered job without one stops the API).
        var schedule = factory.Services.GetRequiredService<IOptions<JobsOptions>>().Value.Schedules[BorrowingDailyJob.Name];
        Assert.True(schedule.Enabled);
        Assert.False(schedule.WorkingDaysOnly);
    }

    [Fact]
    public async Task One_run_marks_overdue_expires_points_and_ends_suspensions_and_a_second_run_changes_nothing()
    {
        DateTime now = factory.Services.GetRequiredService<IClock>().UtcNow;

        // Davit: due 16 days ago → Overdue. Mariam: due in 12 days → stays Active.
        var late = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(13, 1), Members.DavitHakobyan, now.AddDays(-30), 14);
        var onTime = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(14, 1), Members.MariamAvetisyan, now.AddDays(-2), 14);
        // Gor (Restricted in the Staff seed): his only 5 points are past their 90 days → Expired, status Active.
        var gorExpired = PenaltyRecord.Create(Guid.NewGuid(), Members.GorMkrtchyan, late.Id, 5, "Returned late",
            now.AddDays(-95), now.AddDays(-5));
        // Sona (Suspended in the Staff seed): 10 points reached 40 days ago → the 30-day suspension is over, the points
        // still count → Restricted (policy table, BQ-BA-01; start computed from the records, BQ-G2-03).
        var sonaLost = PenaltyRecord.Create(Guid.NewGuid(), Members.SonaVardanyan, late.Id, 10, "Copy lost",
            now.AddDays(-40), now.AddDays(50));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            db.Loans.AddRange(late, onTime);
            db.PenaltyRecords.AddRange(gorExpired, sonaLost);
            await db.SaveChangesAsync();
        }

        // The run through the shared runner, as in production.
        var runner = factory.Services.GetServices<IHostedService>().OfType<DailyJobRunner>().Single(r => r.JobName == BorrowingDailyJob.Name);
        Assert.True(await runner.TryRunAsync(CancellationToken.None));
        Assert.False(await runner.TryRunAsync(CancellationToken.None));   // once per local day

        var first = await SnapshotAsync(late.Id, onTime.Id, gorExpired.Id, sonaLost.Id);
        Assert.Equal(LoanStatus.Overdue, first.Late);
        Assert.Equal(LoanStatus.Active, first.OnTime);
        Assert.Equal(PenaltyRecordStatus.Expired, first.GorRecord);
        Assert.Equal(PenaltyRecordStatus.Active, first.SonaRecord);
        Assert.Equal(StaffMemberStatus.Active, first.Gor);
        Assert.Equal(StaffMemberStatus.Restricted, first.Sona);
        Assert.Equal(1, first.LoanOverdueEvents);
        Assert.Equal(2, first.StaffStatusChangedEvents);
        Assert.Equal((JobRunStatus.Succeeded, 1), first.Run);

        // A second run of the same day (an instance took the run over): nothing changes, nothing is published twice.
        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BorrowingDailyJob>();
            await job.RunAsync(new DailyJobRun(BorrowingDailyJob.Name, DateOnly.FromDateTime(now), now), CancellationToken.None);
        }

        Assert.Equal(first, await SnapshotAsync(late.Id, onTime.Id, gorExpired.Id, sonaLost.Id));
    }

    private sealed record Snapshot(
        LoanStatus Late,
        LoanStatus OnTime,
        PenaltyRecordStatus GorRecord,
        PenaltyRecordStatus SonaRecord,
        StaffMemberStatus Gor,
        StaffMemberStatus Sona,
        int LoanOverdueEvents,
        int StaffStatusChangedEvents,
        (JobRunStatus, int) Run);

    private async Task<Snapshot> SnapshotAsync(Guid late, Guid onTime, Guid gorRecord, Guid sonaRecord)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffMemberService>();

        Task<LoanStatus> Loan(Guid id) => db.Loans.AsNoTracking().Where(l => l.Id == id).Select(l => l.Status).SingleAsync();
        Task<PenaltyRecordStatus> Record(Guid id) =>
            db.PenaltyRecords.AsNoTracking().Where(p => p.Id == id).Select(p => p.Status).SingleAsync();

        // The dispatcher is off in the test host: every published event stays in the outbox.
        var events = await db.OutboxMessages.AsNoTracking().Select(m => m.Type).ToListAsync();
        var run = await db.JobRuns.AsNoTracking().Where(r => r.JobName == BorrowingDailyJob.Name)
            .Select(r => new { r.Status, r.Attempts }).SingleAsync();

        return new Snapshot(
            await Loan(late),
            await Loan(onTime),
            await Record(gorRecord),
            await Record(sonaRecord),
            await staff.GetStatusAsync(Members.GorMkrtchyan),
            await staff.GetStatusAsync(Members.SonaVardanyan),
            events.Count(type => type.EndsWith("LoanOverdue", StringComparison.Ordinal)),
            events.Count(type => type.EndsWith("StaffStatusChanged", StringComparison.Ordinal)),
            (run.Status, run.Attempts));
    }
}
