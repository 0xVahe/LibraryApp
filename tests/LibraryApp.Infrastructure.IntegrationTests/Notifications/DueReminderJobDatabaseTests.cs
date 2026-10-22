using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Notifications.Options;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.IntegrationTests.Time;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Notifications;
using LibraryApp.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

/// <summary>
/// NOT-05 / NOT-06 end to end on SQL Server: the real job, repository, unit of work, renderer, Yerevan clock and
/// working-day calendar, with the default templates of the model. Monday 19 October 2026, 09:00 in Yerevan.
/// Each test gets its own database.
/// </summary>
public sealed class DueReminderJobDatabaseTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static readonly DateTime NowUtc = new(2026, 10, 19, 5, 0, 0, DateTimeKind.Utc);
    private const string ManagerEmail = "manager@ameriabank.am";

    private readonly string _connectionString = TestDatabase.NewDatabase();

    private readonly Guid _dueSoon = Guid.NewGuid();            // due Wed 21 Oct: in 2 working days
    private readonly Guid _overdue5 = Guid.NewGuid();           // due Mon 12 Oct: 5 working days overdue
    private readonly Guid _overdue15 = Guid.NewGuid();          // due Mon 28 Sep: 15 working days overdue, manager in CC
    private readonly Guid _dueInThreeDays = Guid.NewGuid();     // due Thu 22 Oct: in 3 working days, no reminder today
    private readonly Guid _returned = Guid.NewGuid();           // due Wed 21 Oct but returned
    private readonly Guid _deactivatedStaff = Guid.NewGuid();   // due Wed 21 Oct, the employee left the bank

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();   // with the default templates (HasData)

        var departmentId = Guid.NewGuid();
        var staffCategoryId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        db.Departments.Add(new Department(departmentId, "Finance"));
        db.StaffCategories.Add(new StaffCategory(staffCategoryId, "Employee", 3, 21, 1));
        db.Categories.Add(new Category(categoryId, "Technology"));
        db.Books.Add(new Book(bookId, "Clean Code", "9780132350884", 2008, "en", null, categoryId, null));

        StaffMember anna = NewStaff(db, departmentId, staffCategoryId, "FIN-001", ManagerEmail);
        StaffMember left = NewStaff(db, departmentId, staffCategoryId, "FIN-002", null);
        left.Deactivate();

        Loan returned = NewLoan(db, bookId, anna, _returned, Due(2026, 10, 21));
        returned.Return(new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc));
        db.Loans.AddRange(
            NewLoan(db, bookId, anna, _dueSoon, Due(2026, 10, 21)),
            Overdue(NewLoan(db, bookId, anna, _overdue5, Due(2026, 10, 12))),
            Overdue(NewLoan(db, bookId, anna, _overdue15, Due(2026, 9, 28))),
            NewLoan(db, bookId, anna, _dueInThreeDays, Due(2026, 10, 22)),
            NewLoan(db, bookId, left, _deactivatedStaff, Due(2026, 10, 21)),
            returned);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Stores_an_in_app_and_a_Pending_email_row_for_each_reminder()
    {
        string summary = await RunJobAsync();

        Assert.Equal("due soon 1, overdue 2, stored 3, failed 0, skipped 0", summary);

        await using var db = NewContext();
        List<Notification> rows = await db.Notifications.AsNoTracking().ToListAsync();
        Assert.Equal(6, rows.Count);
        Assert.Equal(
            [_dueSoon, _overdue5, _overdue15],
            rows.Select(n => n.LoanId!.Value).Distinct().OrderBy(LoanOrder));

        Assert.All(rows, n =>
        {
            Assert.Equal(Today, n.LocalDate);
            Assert.Equal("fin-001@example.com", n.RecipientEmail);
            Assert.Null(n.EventId);
            Assert.Contains("Clean Code", n.Subject);
            Assert.Equal(
                n.Channel == NotificationChannel.InApp ? NotificationStatus.Sent : NotificationStatus.Pending,
                n.Status);
        });

        Assert.All(rows.Where(n => n.LoanId == _dueSoon), n =>
        {
            Assert.Equal(NotificationType.DueSoonReminder, n.Type);
            Assert.Contains("21.10.2026", n.Body);   // the default template, rendered
        });
        Assert.All(rows.Where(n => n.LoanId != _dueSoon), n => Assert.Equal(NotificationType.OverdueNotice, n.Type));

        // The line manager only on the email of the loan overdue for more than 14 working days.
        Assert.Equal(ManagerEmail, rows.Single(n => n.LoanId == _overdue15 && n.Channel == NotificationChannel.Outlook).CcEmail);
        Assert.All(rows.Where(n => n.LoanId != _overdue15 || n.Channel == NotificationChannel.InApp), n => Assert.Null(n.CcEmail));
    }

    [Fact]
    public async Task A_second_run_on_the_same_day_adds_nothing()
    {
        await RunJobAsync();
        string summary = await RunJobAsync();

        Assert.Equal("due soon 1, overdue 2, stored 0, failed 0, skipped 3", summary);

        await using var db = NewContext();
        Assert.Equal(6, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task The_delivery_job_picks_up_the_reminder_emails()
    {
        await RunJobAsync();

        await using var db = NewContext();
        var store = new NotificationDeliveryStore(db);
        IReadOnlyList<Notification> claimed = await store.ClaimDueAsync(NowUtc, NowUtc.AddMinutes(15), 10);

        Assert.Equal(3, claimed.Count);
        Assert.All(claimed, n => Assert.Equal(NotificationChannel.Outlook, n.Channel));
    }

    // One context per run, like the scope the daily job runner creates.
    private async Task<string> RunJobAsync()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(NowUtc));
        var clock = new SystemClock(time, Options.Create(new TimeZoneOptions { TimeZoneId = "Asia/Yerevan" }));
        // No public holiday between 28 September and 21 October 2026.
        var calendar = new WorkingDayCalendar(Options.Create(new HolidayOptions()));

        await using var db = NewContext();
        var job = new DueReminderJob(
            new ReminderRepository(db),
            new NotificationTemplateRenderer(),
            new UnitOfWork(db),
            new SequentialGuidGenerator(time),
            clock,
            calendar,
            Options.Create(new NotificationsOptions
            {
                Reminders = new RemindersOptions
                {
                    DueSoonWorkingDays = 2,
                    OverdueEveryWorkingDays = 5,
                    LineManagerCcAfterWorkingDays = 14,
                },
            }),
            NullLogger<DueReminderJob>.Instance);

        return await job.RunAsync(new DailyJobRun(DueReminderJob.Name, Today, null), CancellationToken.None);
    }

    private int LoanOrder(Guid loanId) => Array.IndexOf([_dueSoon, _overdue5, _overdue15], loanId);

    private LibraryDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(_connectionString);
        return new LibraryDbContext(options.Options);
    }

    // Noon in Yerevan.
    private static DateTime Due(int year, int month, int day) => new(year, month, day, 8, 0, 0, DateTimeKind.Utc);

    private static StaffMember NewStaff(
        LibraryDbContext db, Guid departmentId, Guid categoryId, string employeeId, string? lineManagerEmail)
    {
        Guid id = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = id,
            UserName = id.ToString("N"),
            NormalizedUserName = id.ToString("N").ToUpperInvariant(),
            Email = $"{id:N}@example.com",
            NormalizedEmail = $"{id:N}@EXAMPLE.COM"
        });

        var staff = new StaffMember(
            id, id, employeeId, "Anna", "Petrosyan",
            $"{employeeId.ToLowerInvariant()}@example.com",
            departmentId, "Analyst", lineManagerEmail, categoryId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        db.StaffMembers.Add(staff);
        return staff;
    }

    // As the Borrowing daily job does at 00:30 (the reminders only read Borrowing's status).
    private static Loan Overdue(Loan loan)
    {
        loan.MarkOverdue(NowUtc);
        return loan;
    }

    private static Loan NewLoan(LibraryDbContext db, Guid bookId, StaffMember staff, Guid loanId, DateTime dueAtUtc)
    {
        var copy = new BookCopy(Guid.NewGuid(), bookId, $"NR-{Guid.NewGuid():N}"[..20], BookCopyCondition.Good, null);
        db.BookCopies.Add(copy);
        return Loan.Issue(loanId, copy.Id, staff.Id, dueAtUtc.AddDays(-21), 21);
    }
}
