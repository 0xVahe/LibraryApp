using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Notifications;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Creates one throw-away database from the model. EnsureCreated applies <c>HasData</c> exactly as a migration with the
/// generated InsertData does, so the test does not depend on the migration the Integration Owner adds.
/// </summary>
public sealed class NotificationTemplatePersistenceFixture : IAsyncLifetime
{
    public ServiceProvider Services { get; } = BuildServices(TestDatabase.NewDatabase());

    private static ServiceProvider BuildServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LibraryDbContext>(options => options.UseLibrarySqlServer(connectionString));
        return services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.EnsureDeletedAsync();

        await Services.DisposeAsync();
    }
}

public class NotificationTemplatePersistenceTests(NotificationTemplatePersistenceFixture fixture)
    : IClassFixture<NotificationTemplatePersistenceFixture>
{
    private static readonly NotificationType[] AllTypes = Enum.GetValues<NotificationType>();

    private LibraryDbContext NewContext() =>
        fixture.Services.CreateScope().ServiceProvider.GetRequiredService<LibraryDbContext>();

    [Fact]
    public void Model_seeds_one_row_per_template_of_the_seed_data()
    {
        using var db = NewContext();

        var seed = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(NotificationTemplate))!.GetSeedData().ToList();

        Assert.Equal(NotificationTemplateSeedData.Templates.Count, seed.Count);
        foreach (var template in NotificationTemplateSeedData.Templates)
        {
            var row = Assert.Single(seed, x => (Guid)x[nameof(NotificationTemplate.Id)]! == template.Id);
            Assert.Equal(template.Type, (NotificationType)row[nameof(NotificationTemplate.Type)]!);
            Assert.Equal(template.Subject, (string)row[nameof(NotificationTemplate.Subject)]!);
            Assert.Equal(template.Body, (string)row[nameof(NotificationTemplate.BodyTemplate)]!);
            Assert.True((bool)row[nameof(NotificationTemplate.IsActive)]!);
        }
    }

    [Fact]
    public async Task Created_database_contains_every_template_unchanged()
    {
        using var db = NewContext();

        var stored = await db.NotificationTemplates.AsNoTracking().OrderBy(x => x.Type).ToListAsync();

        Assert.Equal(AllTypes, stored.Select(x => x.Type));
        Assert.Equal(
            NotificationTemplateSeedData.Templates.OrderBy(x => x.Type).Select(x => (x.Id, x.Type, x.Subject, x.Body, true)),
            stored.Select(x => (x.Id, x.Type, x.Subject, x.BodyTemplate, x.IsActive)));
    }

    [Fact]
    public async Task Stored_templates_are_valid_domain_templates_with_one_active_template_per_type()
    {
        using var db = NewContext();

        var stored = await db.NotificationTemplates.AsNoTracking().ToListAsync();

        Assert.All(stored, x => Assert.Equal(x.Type, new NotificationTemplate(x.Id, x.Type, x.Subject, x.BodyTemplate).Type));
        Assert.Equal(stored.Count, stored.Where(x => x.IsActive).Select(x => x.Type).Distinct().Count());
    }

    [Fact]
    public void Notifications_module_registers_no_seeder_for_templates()
    {
        var services = new ServiceCollection();
        services.AddNotificationsModule(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IModuleSeeder));
    }
}

/// <summary>Seed data; no database needed.</summary>
public class NotificationTemplateSeedDataTests
{
    [Fact]
    public void Covers_every_notification_type_exactly_once()
    {
        var types = NotificationTemplateSeedData.Templates.Select(x => x.Type).ToList();

        Assert.Equal(Enum.GetValues<NotificationType>().OrderBy(x => x), types.OrderBy(x => x));
        Assert.Equal(types.Count, NotificationTemplateSeedData.Templates.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public void Every_template_is_a_valid_domain_template()
    {
        foreach (var t in NotificationTemplateSeedData.Templates)
            Assert.Equal(t.Type, new NotificationTemplate(t.Id, t.Type, t.Subject, t.Body).Type);
    }

    [Fact]
    public void Ids_are_constant()
    {
        Assert.Equal(new Guid("40000000-0000-0000-0001-000000000001"), NotificationTemplateSeedData.IdOf(NotificationType.Welcome));
        Assert.Equal(new Guid("40000000-0000-0000-0001-000000000010"), NotificationTemplateSeedData.IdOf(NotificationType.ReturnReceipt));
    }

    [Fact]
    public void Password_changed_template_has_the_next_constant_id()
    {
        Assert.Equal(new Guid("40000000-0000-0000-0001-000000000011"), NotificationTemplateSeedData.IdOf(NotificationType.PasswordChanged));
    }

    [Fact]
    public void Password_changed_template_uses_only_the_employee_name_and_carries_no_credential_or_link()
    {
        var template = NotificationTemplateSeedData.Templates.Single(x => x.Type == NotificationType.PasswordChanged);
        var text = template.Subject + template.Body;

        Assert.Equal([NotificationPlaceholders.EmployeeName], NotificationPlaceholders.Find(text));
        Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a ", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("administrator", template.Body, StringComparison.OrdinalIgnoreCase);
    }
}
