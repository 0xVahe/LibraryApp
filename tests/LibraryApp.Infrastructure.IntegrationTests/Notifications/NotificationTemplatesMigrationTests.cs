using LibraryApp.Domain.Notifications;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

/// <summary>
/// NOT-02: after the migrations every environment has a default template for every notification type. The migration
/// is idempotent: databases that the former Development seeder filled (the same ids) migrate without an error, and a
/// type that already has a template, possibly edited by an administrator (NOT-09), keeps it.
/// </summary>
public sealed class NotificationTemplatesMigrationTests : IAsyncLifetime
{
    private const string Migration = "AddDefaultNotificationTemplates";

    private readonly string _connectionString = TestDatabase.NewDatabase();

    private LibraryDbContext Open()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(_connectionString);
        return new LibraryDbContext(options.Options);
    }

    private static string PreviousMigration(LibraryDbContext db) => MigrationBefore(db, Migration);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = Open();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Every_type_gets_its_default_template_on_an_empty_database()
    {
        await using (var db = Open())
            await db.Database.MigrateAsync();

        await using var verify = Open();
        var templates = await verify.NotificationTemplates.AsNoTracking().ToListAsync();

        Assert.Equal(Enum.GetValues<NotificationType>().Length, templates.Count);
        foreach (var expected in NotificationTemplateSeedData.Templates)
        {
            var actual = Assert.Single(templates, t => t.Type == expected.Type);
            Assert.Equal((expected.Id, expected.Subject, expected.Body, true),
                (actual.Id, actual.Subject, actual.BodyTemplate, actual.IsActive));
        }
    }

    [Fact]
    public async Task A_database_the_former_seeder_filled_migrates_and_keeps_an_edited_template()
    {
        // As the Development seeder left it: the same ids, and an administrator edited the Welcome text since.
        await using (var db = Open())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration(db));
            foreach (var template in NotificationTemplateSeedData.Templates)
                db.NotificationTemplates.Add(new NotificationTemplate(template.Id, template.Type, template.Subject, template.Body));
            await db.SaveChangesAsync();
            var welcome = await db.NotificationTemplates.SingleAsync(t => t.Type == NotificationType.Welcome);
            welcome.Update("Edited subject", "<p>Edited</p>");
            await db.SaveChangesAsync();
        }

        await using (var db = Open())
            await db.Database.MigrateAsync();   // a plain insert of the same ids would fail here

        await using var verify = Open();
        var templates = await verify.NotificationTemplates.AsNoTracking().ToListAsync();
        Assert.Equal(Enum.GetValues<NotificationType>().Length, templates.Count);
        Assert.Equal("Edited subject", Assert.Single(templates, t => t.Type == NotificationType.Welcome).Subject);
    }

    [Fact]
    public async Task A_type_that_already_has_another_template_keeps_it()
    {
        var own = new NotificationTemplate(Guid.NewGuid(), NotificationType.Welcome, "Own subject", "<p>Own</p>");
        await using (var db = Open())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration(db));
            db.NotificationTemplates.Add(own);
            await db.SaveChangesAsync();
        }

        await using (var db = Open())
            await db.Database.MigrateAsync();   // a second active Welcome template would break UX_NotificationTemplates_Type_Active

        await using var verify = Open();
        var welcome = await verify.NotificationTemplates.AsNoTracking().Where(t => t.Type == NotificationType.Welcome).ToListAsync();
        Assert.Equal(own.Id, Assert.Single(welcome).Id);
        Assert.Equal(Enum.GetValues<NotificationType>().Length, await verify.NotificationTemplates.CountAsync());
    }

    [Fact]
    public async Task Rolling_the_migration_back_removes_only_unchanged_defaults()
    {
        await using (var db = Open())
        {
            await db.Database.MigrateAsync();
            var welcome = await db.NotificationTemplates.SingleAsync(t => t.Type == NotificationType.Welcome);
            welcome.Update("Edited subject", "<p>Edited</p>");
            await db.SaveChangesAsync();
        }

        await using (var db = Open())
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration(db));

        await using var verify = Open();
        var left = await verify.NotificationTemplates.AsNoTracking().ToListAsync();
        Assert.Equal(NotificationType.Welcome, Assert.Single(left).Type);
    }

    private const string PasswordChangedMigration = "AddPasswordChangedTemplate";

    private static string MigrationBefore(LibraryDbContext db, string migration) =>
        db.Database.GetMigrations().TakeWhile(m => !m.EndsWith(migration, StringComparison.Ordinal)).Last();

    [Fact]
    public async Task A_PasswordChanged_template_added_before_the_migration_is_kept()
    {
        var own = new NotificationTemplate(Guid.NewGuid(), NotificationType.PasswordChanged, "Own subject", "<p>Own</p>");
        await using (var db = Open())
        {
            await db.GetService<IMigrator>().MigrateAsync(MigrationBefore(db, PasswordChangedMigration));
            db.NotificationTemplates.Add(own);
            await db.SaveChangesAsync();
        }

        await using (var db = Open())
            await db.Database.MigrateAsync();

        await using var verify = Open();
        var passwordChanged = await verify.NotificationTemplates.AsNoTracking()
            .Where(t => t.Type == NotificationType.PasswordChanged).ToListAsync();
        Assert.Equal(own.Id, Assert.Single(passwordChanged).Id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Rolling_back_the_PasswordChanged_migration_removes_only_an_unchanged_template(bool edited, bool kept)
    {
        await using (var db = Open())
        {
            await db.Database.MigrateAsync();
            if (edited)
            {
                var template = await db.NotificationTemplates.SingleAsync(t => t.Type == NotificationType.PasswordChanged);
                template.Update("Edited subject", "<p>Edited</p>");
                await db.SaveChangesAsync();
            }
        }

        await using (var db = Open())
            await db.GetService<IMigrator>().MigrateAsync(MigrationBefore(db, PasswordChangedMigration));

        await using var verify = Open();
        Assert.Equal(kept, await verify.NotificationTemplates.AnyAsync(t => t.Type == NotificationType.PasswordChanged));
        Assert.Equal(10, await verify.NotificationTemplates.CountAsync(t => t.Type != NotificationType.PasswordChanged));
    }
}
