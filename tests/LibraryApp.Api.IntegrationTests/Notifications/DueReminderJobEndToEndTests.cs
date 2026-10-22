using LibraryApp.Api.IntegrationTests.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Notifications;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Notifications;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Notifications;

/// <summary>
/// NOT-05 / NOT-06 end to end with the registered services: the job resolved from the host, the loans from Borrowing's
/// contract, the titles from Catalog's, the recipients and statuses from Staff's data, the default templates, the
/// Yerevan clock and calendar, the settings of appsettings.json. Monday 19 October 2026; no Borrowing seed.
/// </summary>
public sealed class DueReminderJobEndToEndTests(ApiHostWithoutBorrowingSeed factory) : IClassFixture<ApiHostWithoutBorrowingSeed>
{
    private static readonly DateOnly Monday19 = new(2026, 10, 19);

    [Fact]
    public async Task Stores_an_in_app_and_a_Pending_email_per_reminder_once_and_the_delivery_job_finds_the_emails()
    {
        Guid dueSoon = Guid.NewGuid();          // Anna, due Wed 21 Oct: in 2 working days
        Guid overdue5 = Guid.NewGuid();         // Anna, due Mon 12 Oct: 5 working days overdue
        Guid overdue15 = Guid.NewGuid();        // Anna, due Mon 28 Sep: 15 working days overdue, line manager in CC
        Guid inThreeDays = Guid.NewGuid();      // Anna, due Thu 22 Oct: no reminder today
        Guid returned = Guid.NewGuid();         // Anna, due Wed 21 Oct but returned
        Guid deactivated = Guid.NewGuid();      // Hayk (left the bank), due Wed 21 Oct

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            Loan returnedLoan = Issue(returned, 23, Members.AnnaPetrosyan, Due(2026, 10, 21));
            returnedLoan.Return(DateTime.UtcNow);
            db.Loans.AddRange(
                Issue(dueSoon, 20, Members.AnnaPetrosyan, Due(2026, 10, 21)),
                Overdue(Issue(overdue5, 21, Members.AnnaPetrosyan, Due(2026, 10, 12))),
                Overdue(Issue(overdue15, 22, Members.AnnaPetrosyan, Due(2026, 9, 28))),
                Issue(inThreeDays, 24, Members.AnnaPetrosyan, Due(2026, 10, 22)),
                returnedLoan,
                Issue(deactivated, 25, Members.HaykManukyan, Due(2026, 10, 21)));
            await db.SaveChangesAsync();
        }

        string first = await RunJobAsync();
        string second = await RunJobAsync();

        Assert.Equal("due soon 1, overdue 2, stored 3, failed 0, skipped 0", first);
        Assert.Equal("due soon 1, overdue 2, stored 0, failed 0, skipped 3", second);   // NOT-06

        using var check = factory.Services.CreateScope();
        var services = check.ServiceProvider;
        List<Notification> rows = await services.GetRequiredService<LibraryDbContext>().Notifications.AsNoTracking()
            .Where(n => n.LoanId != null).ToListAsync();
        NotificationRecipient anna = (await services.GetRequiredService<INotificationRecipientReader>().FindAsync(Members.AnnaPetrosyan))!;
        string title = (await services.GetRequiredService<ICatalogService>().GetBookAsync(CatalogSeedIds.Book(20)))!.Title;

        Assert.Equal(6, rows.Count);   // in-app + email for the three reminders, nothing for the others
        Assert.Equal(new HashSet<Guid> { dueSoon, overdue5, overdue15 }, rows.Select(n => n.LoanId!.Value).ToHashSet());
        Assert.All(rows, n =>
        {
            Assert.Equal(Members.AnnaPetrosyan, n.StaffMemberId);
            Assert.Equal(anna.Email, n.RecipientEmail);
            Assert.Equal(Monday19, n.LocalDate);
            Assert.Equal(n.Channel == NotificationChannel.InApp ? NotificationStatus.Sent : NotificationStatus.Pending, n.Status);
        });
        Assert.All(rows.Where(n => n.LoanId == dueSoon), n =>
        {
            Assert.Equal(NotificationType.DueSoonReminder, n.Type);
            Assert.Contains(title, n.Subject + n.Body);   // the title from Catalog
            Assert.Contains("21.10.2026", n.Body);
        });
        Assert.All(rows.Where(n => n.LoanId != dueSoon), n => Assert.Equal(NotificationType.OverdueNotice, n.Type));

        // The line manager only on the email of the loan overdue for more than 14 working days.
        Assert.NotNull(anna.LineManagerEmail);
        Assert.Equal(anna.LineManagerEmail, rows.Single(n => n.LoanId == overdue15 && n.Channel == NotificationChannel.Outlook).CcEmail);
        Assert.All(rows.Where(n => n.LoanId != overdue15 || n.Channel == NotificationChannel.InApp), n => Assert.Null(n.CcEmail));

        // The delivery job (NOT-03) picks the three emails up (the seed may have Pending emails of its own).
        DateTime now = DateTime.UtcNow;
        IReadOnlyList<Notification> claimed =
            await new NotificationDeliveryStore(services.GetRequiredService<LibraryDbContext>()).ClaimDueAsync(now, now.AddMinutes(15), 100);
        Assert.All(
            rows.Where(n => n.Channel == NotificationChannel.Outlook),
            email => Assert.Contains(claimed, n => n.Id == email.Id));
    }

    // One scope per run, like the daily job runner.
    private async Task<string> RunJobAsync()
    {
        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<DueReminderJob>();
        return await job.RunAsync(new DailyJobRun(DueReminderJob.Name, Monday19, null), CancellationToken.None);
    }

    // Noon in Yerevan.
    private static DateTime Due(int year, int month, int day) => new(year, month, day, 8, 0, 0, DateTimeKind.Utc);

    private static Loan Issue(Guid loanId, int book, Guid member, DateTime dueAtUtc) =>
        Loan.Issue(loanId, CatalogSeedIds.Copy(book, 1), member, dueAtUtc.AddDays(-21), 21);

    // As the Borrowing daily job does at 00:30 (the reminders read Borrowing's status through its contract).
    private static Loan Overdue(Loan loan)
    {
        loan.MarkOverdue(DateTime.UtcNow);
        return loan;
    }
}
