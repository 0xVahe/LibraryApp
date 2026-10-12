using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Notifications;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

/// <summary>Migrates one throw-away database; every test starts with an empty NotificationTemplates table.</summary>
public sealed class NotificationTemplateSeederFixture : IAsyncLifetime
{
    public ServiceProvider Services { get; } = BuildServices(TestDatabase.NewConnectionString());

    private static ServiceProvider BuildServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LibraryDbContext>(options => options.UseLibrarySqlServer(connectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<NotificationTemplateSeeder>();
        return services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.EnsureDeletedAsync();

        await Services.DisposeAsync();
    }
}

public class NotificationTemplateSeederTests(NotificationTemplateSeederFixture fixture)
    : IClassFixture<NotificationTemplateSeederFixture>
{
    private static readonly NotificationType[] AllTypes = Enum.GetValues<NotificationType>();

    private async Task ResetAsync()
    {
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().NotificationTemplates.ExecuteDeleteAsync();
    }

    private async Task SeedAsync(CancellationToken ct = default)
    {
        using var scope = fixture.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<NotificationTemplateSeeder>();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .ExecuteInTransactionAsync(token => seeder.SeedAsync(token), ct);
    }

    private async Task<List<NotificationTemplate>> LoadAsync()
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>()
            .NotificationTemplates.AsNoTracking().OrderBy(x => x.Type).ToListAsync();
    }

    private async Task ModifyAsync(Action<LibraryDbContext> change)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        change(db);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Empty_database_receives_one_active_template_per_type()
    {
        await ResetAsync();

        await SeedAsync();

        var templates = await LoadAsync();
        Assert.Equal(AllTypes, templates.Select(x => x.Type));
        Assert.All(templates, x =>
        {
            Assert.True(x.IsActive);
            Assert.Equal(NotificationTemplateSeedData.IdOf(x.Type), x.Id);
        });
    }

    [Fact]
    public async Task Second_run_adds_nothing_and_changes_nothing()
    {
        await ResetAsync();
        await SeedAsync();
        var first = await LoadAsync();

        await SeedAsync();

        var second = await LoadAsync();
        Assert.Equal(first.Select(x => (x.Id, x.Type, x.Subject, x.BodyTemplate, x.IsActive)),
            second.Select(x => (x.Id, x.Type, x.Subject, x.BodyTemplate, x.IsActive)));
    }

    [Fact]
    public async Task Edited_template_is_preserved()
    {
        await ResetAsync();
        await SeedAsync();
        var id = NotificationTemplateSeedData.IdOf(NotificationType.DueSoonReminder);
        await ModifyAsync(db => db.NotificationTemplates.Single(x => x.Id == id).Update("Custom subject", "<p>Custom {{DueDate}}</p>"));

        await SeedAsync();

        var edited = (await LoadAsync()).Single(x => x.Id == id);
        Assert.Equal("Custom subject", edited.Subject);
        Assert.Equal("<p>Custom {{DueDate}}</p>", edited.BodyTemplate);
        Assert.Equal(AllTypes.Length, (await LoadAsync()).Count);
    }

    [Fact]
    public async Task Inactive_template_is_kept_and_not_replaced()
    {
        await ResetAsync();
        await SeedAsync();
        var id = NotificationTemplateSeedData.IdOf(NotificationType.Welcome);
        await ModifyAsync(db => db.NotificationTemplates.Single(x => x.Id == id).Deactivate());

        await SeedAsync();

        var welcome = (await LoadAsync()).Where(x => x.Type == NotificationType.Welcome).ToList();
        var only = Assert.Single(welcome);
        Assert.Equal(id, only.Id);
        Assert.False(only.IsActive);
    }

    [Fact]
    public async Task Administrator_template_with_another_id_blocks_only_its_own_type()
    {
        await ResetAsync();
        var customId = Guid.NewGuid();
        await ModifyAsync(db => db.NotificationTemplates.Add(
            new NotificationTemplate(customId, NotificationType.OverdueNotice, "Own subject", "<p>Own</p>")));

        await SeedAsync();

        var templates = await LoadAsync();
        Assert.Equal(AllTypes.Length, templates.Count);
        var overdue = Assert.Single(templates, x => x.Type == NotificationType.OverdueNotice);
        Assert.Equal(customId, overdue.Id);
        Assert.Equal("Own subject", overdue.Subject);
    }

    [Fact]
    public async Task Only_missing_types_are_added()
    {
        await ResetAsync();
        await SeedAsync();
        var removed = new[] { NotificationType.PenaltyApplied, NotificationType.ReturnReceipt };
        await ModifyAsync(db => db.NotificationTemplates.RemoveRange(db.NotificationTemplates.Where(x => removed.Contains(x.Type))));
        Assert.Equal(AllTypes.Length - 2, (await LoadAsync()).Count);

        await SeedAsync();

        Assert.Equal(AllTypes, (await LoadAsync()).Select(x => x.Type));
    }

    [Fact]
    public async Task Cancelled_token_throws_and_seeds_nothing()
    {
        await ResetAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SeedAsync(cts.Token));

        Assert.Empty(await LoadAsync());
    }
}

/// <summary>Seed data and registration; no database needed.</summary>
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
    public void Seeder_is_registered_as_module_seeder_with_order_40()
    {
        var services = new ServiceCollection();
        services.AddNotificationsModule(new ConfigurationBuilder().Build());

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IModuleSeeder));
        Assert.Equal(typeof(NotificationTemplateSeeder), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(40, new NotificationTemplateSeeder(null!).Order);
    }
}
