using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Notifications;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Notifications;
using LibraryApp.Infrastructure.Persistence.Seed.Notifications;
using LibraryApp.Infrastructure.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

/// <summary>
/// The template administration (NOT-09) on a migrated SQL Server database: activating a template deactivates the previous one
/// before the new one is saved, so the unique index "one active template per type" (G4-08) is never broken.
/// </summary>
public class NotificationTemplateAdminDatabaseTests(StaffSeederFixture fixture) : IClassFixture<StaffSeederFixture>
{
    private NotificationTemplateService Service(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        return new NotificationTemplateService(
            new NotificationTemplateRepository(db), new NotificationTemplateRenderer(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), new SequentialGuidGenerator(TimeProvider.System));
    }

    private async Task<T> RunAsync<T>(Func<NotificationTemplateService, Task<T>> action)
    {
        using var scope = fixture.Services.CreateScope();
        return await action(Service(scope));
    }

    private async Task<List<NotificationTemplate>> StoredAsync(NotificationType type)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().NotificationTemplates.AsNoTracking()
            .Where(t => t.Type == type).ToListAsync();
    }

    [Fact]
    public async Task Activating_a_new_template_replaces_the_seeded_active_one_of_its_type()
    {
        var seeded = Assert.Single(await StoredAsync(NotificationType.ReturnReceipt));
        Assert.True(seeded.IsActive);

        var created = await RunAsync(s => s.CreateAsync(new CreateNotificationTemplateRequest(
            NotificationType.ReturnReceipt, "Receipt for {{BookTitle}}", "<p>{{BookTitle}} was returned on {{ReturnedDate}}.</p>")));
        Assert.False(created.IsActive);
        Assert.Equal(1, (await StoredAsync(NotificationType.ReturnReceipt)).Count(t => t.IsActive));   // nothing changed yet

        await RunAsync(s => s.ActivateAsync(created.Id));

        var stored = await StoredAsync(NotificationType.ReturnReceipt);
        Assert.Equal(2, stored.Count);
        Assert.Equal(created.Id, Assert.Single(stored, t => t.IsActive).Id);
        Assert.False(stored.Single(t => t.Id == seeded.Id).IsActive);
    }

    [Fact]
    public async Task The_new_active_template_is_what_a_real_notification_is_rendered_from()
    {
        var created = await RunAsync(s => s.CreateAsync(new CreateNotificationTemplateRequest(
            NotificationType.PasswordChanged, "Custom security notice", "<p>Hello {{EmployeeName}}, custom text.</p>")));
        await RunAsync(s => s.ActivateAsync(created.Id));

        using var scope = fixture.Services.CreateScope();
        var active = await new NotificationTemplateRepository(scope.ServiceProvider.GetRequiredService<LibraryDbContext>())
            .FindActiveAsync(NotificationType.PasswordChanged);

        Assert.Equal(created.Id, active!.Id);
        Assert.Equal("Custom security notice", active.Subject);
    }

    [Fact]
    public async Task The_active_template_cannot_be_deleted_and_an_inactive_one_can()
    {
        var created = await RunAsync(s => s.CreateAsync(new CreateNotificationTemplateRequest(
            NotificationType.Welcome, "Draft", "<p>Draft {{EmployeeName}}</p>")));
        var active = Assert.Single((await StoredAsync(NotificationType.Welcome)), t => t.IsActive);

        var ex = await Assert.ThrowsAsync<DomainException>(() => RunAsync(async s => { await s.DeleteAsync(active.Id); return 0; }));
        await RunAsync(async s => { await s.DeleteAsync(created.Id); return 0; });

        Assert.Equal(NotificationErrors.TemplateActiveCannotBeDeleted, ex.Error);
        Assert.Equal(active.Id, Assert.Single(await StoredAsync(NotificationType.Welcome)).Id);
    }

    [Fact]
    public async Task Two_active_templates_of_one_type_are_refused_by_the_database()
    {
        var created = await RunAsync(s => s.CreateAsync(new CreateNotificationTemplateRequest(
            NotificationType.AccessRestricted, "Second", "<p>{{Reason}}</p>")));

        // Saving the new one as active without deactivating the old one is what the order of ActivateAsync prevents.
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var tracked = await db.NotificationTemplates.SingleAsync(t => t.Id == created.Id);
        tracked.Activate();

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void The_default_templates_only_use_placeholders_that_are_allowed_for_their_type()
    {
        foreach (var template in NotificationTemplateSeedData.Templates)
        {
            var used = NotificationPlaceholders.Find(template.Subject).Concat(NotificationPlaceholders.Find(template.Body)).Distinct();
            Assert.All(used, name => Assert.Contains(name, NotificationPlaceholders.AllowedFor(template.Type)));
        }
    }
}
