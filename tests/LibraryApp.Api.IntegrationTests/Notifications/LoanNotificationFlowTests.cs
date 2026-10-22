using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Messaging;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Notifications;

/// <summary>
/// The whole way of a loan notification on the real host and SQL Server (NOT-01, NOT-06): a librarian issues a copy through
/// the API, LoanIssued is stored in the outbox with the loan, the outbox dispatcher (switched off in the host, so the test
/// calls it) runs the handler, and the notifications are stored. Every test issues its own copy to its own staff member.
/// <para>
/// The business clock of the host is fixed at a working day in the middle of the day (<see cref="Now"/>), so the NOT-06 rule
/// ("the same type for the same loan once a day") never depends on the Yerevan day changing while a test runs.
/// </para>
/// </summary>
public class LoanNotificationFlowTests : IAsyncLifetime
{
    // Tuesday, 16:00 in Yerevan.
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly FlowHost factory = new();

    public Task InitializeAsync() => factory.InitializeAsync();

    public Task DisposeAsync() => factory.DisposeAsync();

    /// <summary>
    /// A librarian token that is valid at the real time the test runs: the host's own clock is fixed in the past or future
    /// of it, and the API checks the token lifetime against the real time.
    /// </summary>
    private string LibrarianToken() =>
        new JwtTokenIssuer(factory.Services.GetRequiredService<IOptions<JwtOptions>>(), new FlowClock(() => DateTime.UtcNow))
            .Issue(factory.Account(StaffRoles.Librarian)).Token;

    private async Task<LoanDto> IssueAsync(Guid staffMemberId, int book)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LibrarianToken());

        var response = await client.PostAsJsonAsync("/api/loans", new IssueLoanRequest(staffMemberId, CatalogSeedIds.CopyBarcode(book, 1)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoanDto>(Json))!;
    }

    private Task DeliverAsync() =>
        factory.Services.GetRequiredService<OutboxDispatcher>().DeliverDueAsync(CancellationToken.None);

    private async Task<T> Db<T>(Func<LibraryDbContext, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
    }

    private Task<List<Notification>> NotificationsOfAsync(Guid loanId) =>
        Db(db => db.Notifications.AsNoTracking().Where(n => n.LoanId == loanId).OrderBy(n => n.Type).ThenBy(n => n.Channel).ToListAsync());

    private Task<OutboxMessage> OutboxMessageAsync(Guid eventId) =>
        Db(db => db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == eventId));

    private Task<Guid> LoanIssuedEventIdAsync(Guid loanId) =>
        Db(db => db.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Type.Contains(nameof(LoanIssued)) && m.Payload.Contains(loanId.ToString()))
            .Select(m => m.Id).SingleAsync());

    [Fact]
    public async Task An_issued_loan_gets_the_confirmation_on_both_channels_and_the_calendar_item_on_outlook()
    {
        var loan = await IssueAsync(Members.MariamAvetisyan, book: 4);
        var eventId = await LoanIssuedEventIdAsync(loan.LoanId);

        await DeliverAsync();

        var message = await OutboxMessageAsync(eventId);
        Assert.NotNull(message.ProcessedAtUtc);
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.LastError);

        var rows = await NotificationsOfAsync(loan.LoanId);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, n =>
        {
            Assert.Equal(Members.MariamAvetisyan, n.StaffMemberId);
            Assert.Equal(eventId, n.EventId);
            Assert.Equal(loan.LoanId, n.LoanId);
            Assert.Equal(Now, n.CreatedAtUtc);                              // the host clock is fixed: the NOT-06 day never moves
            Assert.Equal(new DateOnly(2026, 10, 20), n.LocalDate);
        });

        var title = await Db(db => db.Books.AsNoTracking().Where(b => b.Id == CatalogSeedIds.Book(4)).Select(b => b.Title).SingleAsync());
        var confirmationOutlook = rows.Single(n => n.Type == NotificationType.LoanConfirmation && n.Channel == NotificationChannel.Outlook);
        Assert.Equal(NotificationStatus.Pending, confirmationOutlook.Status);   // sent later by the delivery job
        Assert.Contains(title, confirmationOutlook.Subject);
        Assert.Contains("Mariam", confirmationOutlook.Body);
        Assert.DoesNotContain("{{", confirmationOutlook.Body);

        var confirmationInApp = rows.Single(n => n.Type == NotificationType.LoanConfirmation && n.Channel == NotificationChannel.InApp);
        Assert.Equal(NotificationStatus.Sent, confirmationInApp.Status);

        var calendar = rows.Single(n => n.Type == NotificationType.DueDateCalendarItem);
        Assert.Equal(NotificationChannel.Outlook, calendar.Channel);
        Assert.Equal(NotificationStatus.Pending, calendar.Status);
    }

    [Fact]
    public async Task A_redelivered_event_creates_no_second_set_of_notifications()
    {
        var loan = await IssueAsync(Members.MariamAvetisyan, book: 5);
        var eventId = await LoanIssuedEventIdAsync(loan.LoanId);
        await DeliverAsync();

        // Deliver the same message once more, as after a crash between the handlers and the "processed" mark.
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Set<OutboxMessage>()
                .Where(m => m.Id == eventId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAtUtc, (DateTime?)null));
        await DeliverAsync();

        Assert.Equal(3, (await NotificationsOfAsync(loan.LoanId)).Count);
        var message = await OutboxMessageAsync(eventId);
        Assert.NotNull(message.ProcessedAtUtc);
        Assert.Equal(0, message.Attempts);
    }

    [Fact]
    public async Task A_second_event_for_the_same_loan_on_the_same_day_is_skipped_by_the_database_rule_without_a_retry()
    {
        var loan = await IssueAsync(Members.MariamAvetisyan, book: 6);
        await DeliverAsync();
        Assert.Equal(3, (await NotificationsOfAsync(loan.LoanId)).Count);
        var firstEventId = await LoanIssuedEventIdAsync(loan.LoanId);

        // Another LoanIssued of the same loan, as a publisher bug or a second source would produce (NOT-06).
        var second = await PublishLoanIssuedAsync(loan);
        await DeliverAsync();

        var rows = await NotificationsOfAsync(loan.LoanId);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, n => Assert.Equal(firstEventId, n.EventId));
        var message = await OutboxMessageAsync(second.EventId);
        Assert.NotNull(message.ProcessedAtUtc);   // processed, not retried
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.LastError);
    }

    [Fact]
    public async Task A_returned_loan_gets_the_return_receipt_in_app_only()
    {
        var loan = await IssueAsync(Members.MariamAvetisyan, book: 7);
        using (var client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LibrarianToken());
            var response = await client.PostAsJsonAsync($"/api/loans/{loan.LoanId}/return", new ReturnLoanRequest(false, null));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await DeliverAsync();

        var receipt = (await NotificationsOfAsync(loan.LoanId)).Where(n => n.Type == NotificationType.ReturnReceipt).ToList();
        var row = Assert.Single(receipt);                          // in-app only (notification catalogue)
        Assert.Equal(NotificationChannel.InApp, row.Channel);
        Assert.Equal(NotificationStatus.Sent, row.Status);
        Assert.Equal(Members.MariamAvetisyan, row.StaffMemberId);
        Assert.DoesNotContain("{{", row.Body);
    }

    private async Task<LoanIssued> PublishLoanIssuedAsync(LoanDto loan)
    {
        using var scope = factory.Services.CreateScope();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var domainEvent = new LoanIssued(
            ids.NewId(), clock.UtcNow, loan.LoanId, loan.StaffMemberId, loan.BookCopyId, CatalogSeedIds.Book(6), loan.DueAtUtc);
        await unitOfWork.ExecuteInTransactionAsync(token => bus.PublishAsync(domainEvent, token));
        return domainEvent;
    }

    /// <summary>
    /// The API host without the Borrowing seed (like the shared <c>ApiHostWithoutBorrowingSeed</c>, which stays unchanged for
    /// the Borrowing tests) and with the business clock fixed at <see cref="Now"/>.
    /// </summary>
    private sealed class FlowHost : ApiHostFactory
    {
        protected override void ConfigureTestServices(IServiceCollection services)
        {
            var borrowingSeed = services.Single(d => d.ServiceType == typeof(IModuleSeeder) && d.ImplementationType == typeof(BorrowingSeeder));
            services.Remove(borrowingSeed);

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new FlowClock(() => Now));
        }
    }

    /// <summary>A clock in the library time zone (Asia/Yerevan): NotificationCreator takes the NOT-06 day from ToLocalDate.</summary>
    private sealed class FlowClock(Func<DateTime> now) : IClock
    {
        private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Yerevan");

        public DateTime UtcNow => now();

        public DateOnly LocalToday => ToLocalDate(now());

        public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);

        public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

        public DateTime StartOfLocalDayUtc(DateOnly localDate) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), Zone);

        public DateTime EndOfLocalDayUtc(DateOnly localDate) => StartOfLocalDayUtc(localDate.AddDays(1)).AddTicks(-1);
    }
}
