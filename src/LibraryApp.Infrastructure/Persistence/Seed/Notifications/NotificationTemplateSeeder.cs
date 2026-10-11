using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Seed.Notifications;

/// <summary>
/// Default notification templates (NOT-02, requirements section 9). Idempotent: a type that already has any template,
/// active or inactive and possibly edited by an administrator (NOT-09), is left alone; only missing types are added.
/// Runs inside the transaction opened by <see cref="DatabaseInitializer"/>, which saves the changes.
/// </summary>
public sealed class NotificationTemplateSeeder(LibraryDbContext db) : IModuleSeeder
{
    public int Order => 40;

    public string Module => "Notifications";

    public async Task SeedAsync(CancellationToken ct)
    {
        var existingTypes = (await db.NotificationTemplates.Select(x => x.Type).Distinct().ToListAsync(ct)).ToHashSet();

        foreach (var template in NotificationTemplateSeedData.Templates.Where(x => !existingTypes.Contains(x.Type)))
            db.NotificationTemplates.Add(new NotificationTemplate(template.Id, template.Type, template.Subject, template.Body));
    }
}
