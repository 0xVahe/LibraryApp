using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

/// <summary>
/// NOT-05 data: open loans due 10 (marked Overdue), 20 and 30 October (the last one of a deactivated employee), a
/// returned loan due 10 October, the default templates (from the model) with OverdueNotice made inactive, and two
/// DueSoonReminder notifications of the loan due 20 October, on 15 and 19 October.
/// </summary>
public sealed class ReminderDatabase : IAsyncLifetime
{
    public static readonly Guid DueOct10 = Guid.NewGuid();
    public static readonly Guid DueOct20 = Guid.NewGuid();
    public static readonly Guid DeactivatedDueOct30 = Guid.NewGuid();
    public static readonly Guid ReturnedDueOct10 = Guid.NewGuid();
    public static readonly DateOnly EarlierNotified = new(2026, 10, 15);
    public static readonly DateOnly Notified = new(2026, 10, 19);

    public string ConnectionString { get; } = TestDatabase.NewDatabase();

    public LibraryDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(ConnectionString);
        return new LibraryDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        var departmentId = Guid.NewGuid();
        var staffCategoryId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        db.Departments.Add(new Department(departmentId, "Finance"));
        db.StaffCategories.Add(new StaffCategory(staffCategoryId, "Employee", 3, 21, 1));
        db.Categories.Add(new Category(categoryId, "Technology"));
        db.Books.Add(new Book(bookId, "Clean Code", "9780132350884", 2008, "en", null, categoryId, null));

        StaffMember active = NewStaff(db, departmentId, staffCategoryId, "FIN-001", "manager@ameriabank.am");
        StaffMember left = NewStaff(db, departmentId, staffCategoryId, "FIN-002", null);
        left.Deactivate();

        Loan returned = NewLoan(db, bookId, active, ReturnedDueOct10, Due(2026, 10, 10));
        returned.Return(new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));
        Loan overdueLoan = NewLoan(db, bookId, active, DueOct10, Due(2026, 10, 10));
        overdueLoan.MarkOverdue(new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc));
        db.Loans.AddRange(
            overdueLoan,
            NewLoan(db, bookId, active, DueOct20, Due(2026, 10, 20)),
            NewLoan(db, bookId, left, DeactivatedDueOct30, Due(2026, 10, 30)),
            returned);

        // The default templates are part of the model (HasData), so they exist already: at most one active per type.
        NotificationTemplate overdue = await db.NotificationTemplates.SingleAsync(t => t.Type == NotificationType.OverdueNotice);
        overdue.Deactivate();

        foreach (DateOnly date in new[] { EarlierNotified, Notified })
        {
            db.Notifications.Add(Notification.Create(
                Guid.NewGuid(), active.Id, NotificationType.DueSoonReminder, NotificationChannel.InApp,
                active.CorporateEmail, null, "Due soon", "<p>Due</p>",
                date.ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc), date, loanId: DueOct20));
        }

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
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

    private static Loan NewLoan(LibraryDbContext db, Guid bookId, StaffMember staff, Guid loanId, DateTime dueAtUtc)
    {
        var copy = new BookCopy(Guid.NewGuid(), bookId, $"NR-{Guid.NewGuid():N}"[..20], BookCopyCondition.Good, null);
        db.BookCopies.Add(copy);
        return Loan.Issue(loanId, copy.Id, staff.Id, dueAtUtc.AddDays(-21), 21);
    }
}

public sealed class ReminderRepositoryTests(ReminderDatabase database) : IClassFixture<ReminderDatabase>
{
    private static readonly NotificationType[] Types = [NotificationType.DueSoonReminder, NotificationType.OverdueNotice];

    [Fact]
    public async Task Open_loans_in_the_range_with_staff_and_book()
    {
        await using var db = database.NewContext();
        var repository = new ReminderRepository(db);

        var loans = await repository.GetOpenLoansDueAsync(
            new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc));

        var loan = Assert.Single(loans);
        Assert.Equal(ReminderDatabase.DueOct20, loan.LoanId);
        Assert.Equal("Anna Petrosyan", loan.EmployeeName);
        Assert.Equal("fin-001@example.com", loan.CorporateEmail);
        Assert.Equal("manager@ameriabank.am", loan.LineManagerEmail);
        Assert.Equal("Clean Code", loan.BookTitle);
    }

    [Fact]
    public async Task Overdue_loans_are_the_ones_Borrowing_marked_Overdue_not_returned_or_only_past_due()
    {
        await using var db = database.NewContext();
        var repository = new ReminderRepository(db);

        var loans = await repository.GetOverdueLoansAsync();

        Assert.Equal(ReminderDatabase.DueOct10, Assert.Single(loans).LoanId);
    }

    [Fact]
    public async Task Loans_of_staff_who_left_the_bank_are_left_out()
    {
        await using var db = database.NewContext();
        var repository = new ReminderRepository(db);

        var loans = await repository.GetOpenLoansDueAsync(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        Assert.DoesNotContain(loans, loan => loan.LoanId == ReminderDatabase.DeactivatedDueOct30);
        Assert.Equal([ReminderDatabase.DueOct10, ReminderDatabase.DueOct20], loans.Select(loan => loan.LoanId));
    }

    [Fact]
    public async Task Only_active_templates()
    {
        await using var db = database.NewContext();
        var repository = new ReminderRepository(db);

        var templates = await repository.GetTemplatesAsync(Types);

        Assert.Equal(NotificationType.DueSoonReminder, Assert.Single(templates).Type);
    }

    [Fact]
    public async Task Notifications_already_stored_on_the_date()
    {
        await using var db = database.NewContext();
        var repository = new ReminderRepository(db);

        var onDate = await repository.GetLoanNotificationsOnAsync(ReminderDatabase.Notified, Types);
        var nextDay = await repository.GetLoanNotificationsOnAsync(ReminderDatabase.Notified.AddDays(1), Types);

        Assert.Equal((ReminderDatabase.DueOct20, NotificationType.DueSoonReminder), Assert.Single(onDate));
        Assert.Empty(nextDay);
    }

    [Fact]
    public async Task Last_notification_date_per_loan_and_type_before_a_date()
    {
        await using var db = database.NewContext();
        var repository = new ReminderRepository(db);
        Guid[] loanIds = [ReminderDatabase.DueOct10, ReminderDatabase.DueOct20];

        var beforeNextDay = await repository.GetLastLoanNotificationDatesAsync(loanIds, Types, ReminderDatabase.Notified.AddDays(1));
        var beforeNotified = await repository.GetLastLoanNotificationDatesAsync(loanIds, Types, ReminderDatabase.Notified);
        var otherType = await repository.GetLastLoanNotificationDatesAsync(
            loanIds, [NotificationType.OverdueNotice], ReminderDatabase.Notified.AddDays(1));

        var entry = Assert.Single(beforeNextDay);
        Assert.Equal((ReminderDatabase.DueOct20, NotificationType.DueSoonReminder), entry.Key);
        Assert.Equal(ReminderDatabase.Notified, entry.Value);
        Assert.Equal(ReminderDatabase.EarlierNotified, Assert.Single(beforeNotified).Value);
        Assert.Empty(otherType);
    }
}
