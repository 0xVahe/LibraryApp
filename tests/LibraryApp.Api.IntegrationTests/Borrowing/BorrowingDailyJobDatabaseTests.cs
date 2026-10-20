using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
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
    public async Task Expired_pickup_transfers_its_copy_to_the_next_waiter_once()
    {
        DateTime now = factory.Services.GetRequiredService<IClock>().UtcNow;
        Guid bookId = CatalogSeedIds.Book(1);
        Guid copyId = CatalogSeedIds.Copy(1, 1);

        Reservation expired = Reservation.Create(
            Guid.NewGuid(),
            bookId,
            Members.AnnaPetrosyan,
            now.AddDays(-10),
            queuePosition: 1);
        expired.MarkReady(copyId, now.AddTicks(-1));

        Reservation waiting = Reservation.Create(
            Guid.NewGuid(),
            bookId,
            Members.DavitHakobyan,
            now.AddDays(-9),
            queuePosition: 1);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var copy = await db.BookCopies.SingleAsync(c => c.Id == copyId);
            copy.ChangeStatus(BookCopyStatus.Reserved);
            db.Reservations.AddRange(expired, waiting);
            await db.SaveChangesAsync();
        }

        int readyEventsBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            readyEventsBefore = (await db.OutboxMessages.AsNoTracking()
                .Select(message => message.Type)
                .ToListAsync())
                .Count(type => type.EndsWith("ReservationReady", StringComparison.Ordinal));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BorrowingDailyJob>();
            var run = new DailyJobRun(
                BorrowingDailyJob.Name,
                DateOnly.FromDateTime(now),
                now);

            await job.RunAsync(run, CancellationToken.None);
            await job.RunAsync(run, CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var savedExpired = await db.Reservations.AsNoTracking()
                .SingleAsync(r => r.Id == expired.Id);
            var savedWaiting = await db.Reservations.AsNoTracking()
                .SingleAsync(r => r.Id == waiting.Id);
            var savedCopy = await db.BookCopies.AsNoTracking()
                .SingleAsync(c => c.Id == copyId);
            int readyEventsAfter = (await db.OutboxMessages.AsNoTracking()
                .Select(message => message.Type)
                .ToListAsync())
                .Count(type => type.EndsWith("ReservationReady", StringComparison.Ordinal));

            Assert.Equal(ReservationStatus.Expired, savedExpired.Status);
            Assert.Equal(copyId, savedExpired.CopyId);
            Assert.Equal(ReservationStatus.ReadyForPickup, savedWaiting.Status);
            Assert.Equal(copyId, savedWaiting.CopyId);
            Assert.Equal(BookCopyStatus.Reserved, savedCopy.Status);
            Assert.Equal(readyEventsBefore + 1, readyEventsAfter);
        }
    }

    [Fact]
    public async Task A_failed_pickup_expiry_rolls_back_alone_and_the_run_reports_the_failure()
    {
        DateTime now = factory.Services.GetRequiredService<IClock>().UtcNow;

        // Book 2: the hand-over to the next member fails after the expired row is flushed (a waiting reservation
        // created "in the future" cannot accept a deadline before its creation), so its transaction must roll back.
        Guid brokenCopy = CatalogSeedIds.Copy(2, 1);
        Reservation brokenExpired = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(2), Members.TigranMelikyan,
            now.AddDays(-10), queuePosition: 1);
        brokenExpired.MarkReady(brokenCopy, now.AddTicks(-1));
        Reservation brokenWaiter = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(2), Members.EvaHovsepyan,
            now.AddYears(1), queuePosition: 1);

        // Book 3: an expired pickup with nobody waiting, processed in the same run.
        Guid freeCopy = CatalogSeedIds.Copy(3, 1);
        Reservation expired = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(3), Members.LusineKarapetyan,
            now.AddDays(-10), queuePosition: 1);
        expired.MarkReady(freeCopy, now.AddTicks(-1));

        await WithDbAsync(async db =>
        {
            (await db.BookCopies.SingleAsync(c => c.Id == brokenCopy)).ChangeStatus(BookCopyStatus.Reserved);
            (await db.BookCopies.SingleAsync(c => c.Id == freeCopy)).ChangeStatus(BookCopyStatus.Reserved);
            db.Reservations.AddRange(brokenExpired, brokenWaiter, expired);
            await db.SaveChangesAsync();
        });

        int readyEventsBefore = await ReadyEventsAsync();

        try
        {
            using var scope = factory.Services.CreateScope();
            var job = scope.ServiceProvider.GetRequiredService<BorrowingDailyJob>();

            // The run reports the failure, so the shared runner retries it (as for loans and staff).
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                job.RunAsync(new DailyJobRun(BorrowingDailyJob.Name, DateOnly.FromDateTime(now), now), CancellationToken.None));
            Assert.Contains("failures: 1", failure.Message);

            await WithDbAsync(async db =>
            {
                // Book 2: nothing of the failed transaction is saved, the flushed Expired status included.
                var savedBroken = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == brokenExpired.Id);
                Assert.Equal(ReservationStatus.ReadyForPickup, savedBroken.Status);
                Assert.Equal(brokenCopy, savedBroken.CopyId);
                var savedWaiter = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == brokenWaiter.Id);
                Assert.Equal((ReservationStatus.Waiting, 1), (savedWaiter.Status, savedWaiter.QueuePosition));
                Assert.Equal(BookCopyStatus.Reserved,
                    (await db.BookCopies.AsNoTracking().SingleAsync(c => c.Id == brokenCopy)).Status);

                // Book 3: expired in its own transaction despite the failure of book 2; the copy is back on the shelf.
                Assert.Equal(ReservationStatus.Expired,
                    (await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == expired.Id)).Status);
                Assert.Equal(BookCopyStatus.Available,
                    (await db.BookCopies.AsNoTracking().SingleAsync(c => c.Id == freeCopy)).Status);
            });

            Assert.Equal(readyEventsBefore, await ReadyEventsAsync());
        }
        finally
        {
            // Remove the broken waiter: the next run of the shared database expires book 2 normally.
            await WithDbAsync(async db =>
            {
                db.Reservations.Remove(await db.Reservations.SingleAsync(r => r.Id == brokenWaiter.Id));
                await db.SaveChangesAsync();
            });
        }
    }

    [Fact]
    public async Task Collecting_and_expiring_the_same_pickup_at_once_gives_the_copy_to_one_side_only()
    {
        DateTime now = factory.Services.GetRequiredService<IClock>().UtcNow;
        Guid bookId = CatalogSeedIds.Book(4);
        Guid copyId = CatalogSeedIds.Copy(4, 1);

        Reservation held = Reservation.Create(Guid.NewGuid(), bookId, Members.NarekSimonyan, now.AddDays(-10), queuePosition: 1);
        held.MarkReady(copyId, now.AddTicks(-1));
        Reservation next = Reservation.Create(Guid.NewGuid(), bookId, Members.AniSahakyan, now.AddDays(-9), queuePosition: 1);

        await WithDbAsync(async db =>
        {
            (await db.BookCopies.SingleAsync(c => c.Id == copyId)).ChangeStatus(BookCopyStatus.Reserved);
            db.Reservations.AddRange(held, next);
            await db.SaveChangesAsync();
        });

        int readyEventsBefore = await ReadyEventsAsync();

        // The member collects the held copy at the desk while the job expires the same reservation.
        Task<bool> expiry = Task.Run(async () =>
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IBorrowingDailyJobService>().ExpirePickupAsync(held.Id);
        });
        Task<DomainException?> issue = Task.Run(async () =>
        {
            using var scope = factory.Services.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<ILoanService>()
                    .IssueAsync(new IssueLoanRequest(Members.NarekSimonyan, CatalogSeedIds.CopyBarcode(4, 1)));
                return null;
            }
            catch (DomainException exception)
            {
                return exception;
            }
        });

        bool expired = await expiry;
        DomainException? issueError = await issue;

        await WithDbAsync(async db =>
        {
            var savedHeld = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == held.Id);
            var savedNext = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == next.Id);
            var copy = await db.BookCopies.AsNoTracking().SingleAsync(c => c.Id == copyId);
            int loans = await db.Loans.AsNoTracking().CountAsync(l => l.BookCopyId == copyId);

            if (expired)
            {
                // The job won the book lock: the copy is held for the next member, the late collection is refused.
                Assert.Equal(BorrowingErrors.CopyNotAvailable.Code, issueError?.Error.Code);
                Assert.Equal(ReservationStatus.Expired, savedHeld.Status);
                Assert.Equal((ReservationStatus.ReadyForPickup, (Guid?)copyId), (savedNext.Status, savedNext.CopyId));
                Assert.Equal(BookCopyStatus.Reserved, copy.Status);
                Assert.Equal(0, loans);
            }
            else
            {
                // The desk won: the loan is issued and the job leaves the fulfilled reservation alone.
                Assert.Null(issueError);
                Assert.Equal(ReservationStatus.Fulfilled, savedHeld.Status);
                Assert.Equal(ReservationStatus.Waiting, savedNext.Status);
                Assert.Equal(BookCopyStatus.OnLoan, copy.Status);
                Assert.Equal(1, loans);
            }
        });

        Assert.Equal(readyEventsBefore + (expired ? 1 : 0), await ReadyEventsAsync());
    }

    private async Task WithDbAsync(Func<LibraryDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
    }

    // The dispatcher is off in the test host: every published event stays in the outbox.
    private async Task<int> ReadyEventsAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().OutboxMessages
            .AsNoTracking()
            .CountAsync(message => message.Type.EndsWith("ReservationReady"));
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
