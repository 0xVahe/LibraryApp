using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Notifications.EventHandlers;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;
using LibraryApp.Infrastructure.IntegrationTests.Identity;
using LibraryApp.Infrastructure.IntegrationTests.Persistence;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Notifications;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

/// <summary>
/// StaffEnrolled → Welcome on a migrated, seeded SQL Server database: the real repositories, the default template from
/// the migrations, the real unit of work (the handler only adds rows; the unit of work saves them, as in the dispatcher).
/// </summary>
public class StaffEnrolledWelcomeDatabaseTests(StaffSeederFixture fixture) : IClassFixture<StaffSeederFixture>
{
    private static readonly Guid Anna = StaffSeedIds.StaffMembers.AnnaPetrosyan;
    private static readonly DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private async Task HandleAsync(StaffEnrolled domainEvent)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var creator = new NotificationCreator(
            new NotificationRepository(db),
            new NotificationTemplateRepository(db),
            new NotificationRecipientReader(db),
            new NotificationTemplateRenderer(),
            new SequentialGuidGenerator(TimeProvider.System),
            new FixedClock(Now));
        var handler = new StaffEnrolledEventHandler(creator);

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .ExecuteInTransactionAsync(token => handler.HandleAsync(domainEvent, token));
    }

    private async Task<List<Notification>> RowsOfAsync(Guid eventId)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Notifications.AsNoTracking()
            .Where(n => n.EventId == eventId).ToListAsync();
    }

    [Fact]
    public async Task Welcome_is_stored_for_both_channels_with_the_event_id_and_the_seeded_template()
    {
        var domainEvent = new StaffEnrolled(Guid.NewGuid(), Now, Anna);

        await HandleAsync(domainEvent);

        var rows = await RowsOfAsync(domainEvent.EventId);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, n =>
        {
            Assert.Equal(NotificationType.Welcome, n.Type);
            Assert.Equal(Anna, n.StaffMemberId);
            Assert.Equal("Welcome to the Ameriabank library", n.Subject);
            Assert.Contains("Dear Anna Petrosyan,", n.Body);
            Assert.DoesNotContain("{{", n.Body);
            Assert.EndsWith("@ameriabank.am", n.RecipientEmail);
        });
        Assert.Equal(NotificationStatus.Pending, rows.Single(n => n.Channel == NotificationChannel.Outlook).Status);
        Assert.Equal(NotificationStatus.Sent, rows.Single(n => n.Channel == NotificationChannel.InApp).Status);
    }

    [Fact]
    public async Task Handling_the_same_event_again_adds_no_rows()
    {
        var domainEvent = new StaffEnrolled(Guid.NewGuid(), Now, Anna);

        await HandleAsync(domainEvent);
        await HandleAsync(domainEvent);

        Assert.Equal(2, (await RowsOfAsync(domainEvent.EventId)).Count);
    }

    [Fact]
    public async Task An_unknown_staff_member_stores_nothing_and_reports_recipient_not_found()
    {
        var domainEvent = new StaffEnrolled(Guid.NewGuid(), Now, Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<DomainException>(() => HandleAsync(domainEvent));

        Assert.Equal(NotificationErrors.RecipientNotFound, ex.Error);
        Assert.Empty(await RowsOfAsync(domainEvent.EventId));
    }
}
