using LibraryApp.Application.Notifications.Services;
using LibraryApp.Infrastructure.Persistence.Seed.Notifications;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

/// <summary>The seeded templates (G4-12) and the renderer (NOT-02) use the same placeholder names.</summary>
public class SeedTemplateRenderingTests
{
    private static readonly string[] KnownPlaceholders =
    [
        NotificationPlaceholders.EmployeeName, NotificationPlaceholders.BookTitle, NotificationPlaceholders.DueDate,
        NotificationPlaceholders.ReadyUntil, NotificationPlaceholders.Points, NotificationPlaceholders.TotalPoints,
        NotificationPlaceholders.Reason, NotificationPlaceholders.EffectiveUntil, NotificationPlaceholders.ReturnedDate,
    ];

    public static TheoryData<string> Types()
    {
        var data = new TheoryData<string>();
        foreach (var template in NotificationTemplateSeedData.Templates)
            data.Add(template.Type.ToString());
        return data;
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Seed_template_renders(string type)
    {
        var template = NotificationTemplateSeedData.Templates.Single(t => t.Type.ToString() == type);
        var values = KnownPlaceholders.ToDictionary(name => name, name => $"[{name}]");

        var result = new NotificationTemplateRenderer().Render(template.Subject, template.Body, values);

        Assert.DoesNotContain("{{", result.Subject);
        Assert.DoesNotContain("{{", result.HtmlBody);
        Assert.All(NotificationPlaceholders.Find(template.Subject + template.Body), name => Assert.Contains(name, KnownPlaceholders));
    }
}
