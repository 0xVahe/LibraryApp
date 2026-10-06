using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Tests.Staff;

namespace LibraryApp.Domain.Tests.Notifications;

public sealed class NotificationTemplateTests
{
    private const string Body = "<p>Dear {{EmployeeName}}, please return {{BookTitle}} by {{DueDate}}.</p>";

    [Fact]
    public void New_template_is_active_and_keeps_its_texts()
    {
        var template = new NotificationTemplate(Guid.NewGuid(), NotificationType.DueSoonReminder, "  Book due soon  ", Body);

        Assert.Equal(NotificationType.DueSoonReminder, template.Type);
        Assert.Equal("Book due soon", template.Subject);
        Assert.Equal(Body, template.BodyTemplate);
        Assert.True(template.IsActive);
    }

    [Fact]
    public void New_template_can_be_created_inactive()
    {
        var template = new NotificationTemplate(Guid.NewGuid(), NotificationType.Welcome, "Welcome", Body, isActive: false);

        Assert.False(template.IsActive);
    }

    [Fact]
    public void New_template_rejects_an_empty_id()
    {
        Assert.Throws<ArgumentException>(
            () => new NotificationTemplate(Guid.Empty, NotificationType.Welcome, "Welcome", Body));
    }

    [Fact]
    public void Update_changes_subject_and_body()
    {
        var template = new NotificationTemplate(Guid.NewGuid(), NotificationType.Welcome, "Welcome", Body);

        template.Update("Welcome to the library", "<p>Hello {{EmployeeName}}</p>");

        Assert.Equal("Welcome to the library", template.Subject);
        Assert.Equal("<p>Hello {{EmployeeName}}</p>", template.BodyTemplate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_rejects_a_missing_subject(string subject)
    {
        var template = new NotificationTemplate(Guid.NewGuid(), NotificationType.Welcome, "Welcome", Body);

        DomainAssert.ThrowsError(
            NotificationErrors.TemplateSubjectRequired,
            () => template.Update(subject, Body));
    }

    [Fact]
    public void Update_rejects_a_subject_longer_than_300_characters()
    {
        var template = new NotificationTemplate(Guid.NewGuid(), NotificationType.Welcome, "Welcome", Body);

        DomainAssert.ThrowsError(
            NotificationErrors.TemplateSubjectTooLong,
            () => template.Update(new string('a', NotificationTemplate.SubjectMaxLength + 1), Body));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_rejects_a_missing_body(string body)
    {
        var template = new NotificationTemplate(Guid.NewGuid(), NotificationType.Welcome, "Welcome", Body);

        DomainAssert.ThrowsError(
            NotificationErrors.TemplateBodyRequired,
            () => template.Update("Welcome", body));
    }

    [Fact]
    public void Failed_update_keeps_the_old_texts()
    {
        var template = new NotificationTemplate(Guid.NewGuid(), NotificationType.Welcome, "Welcome", Body);

        Assert.ThrowsAny<Exception>(() => template.Update("New subject", ""));

        Assert.Equal("Welcome", template.Subject);
        Assert.Equal(Body, template.BodyTemplate);
    }

    [Fact]
    public void Deactivate_and_Activate_switch_the_flag()
    {
        var template = new NotificationTemplate(Guid.NewGuid(), NotificationType.Welcome, "Welcome", Body);

        template.Deactivate();
        Assert.False(template.IsActive);

        template.Activate();
        Assert.True(template.IsActive);
    }
}
