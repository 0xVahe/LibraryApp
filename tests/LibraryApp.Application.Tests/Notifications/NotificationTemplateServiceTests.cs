using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Application.Notifications.Validators;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class NotificationTemplateServiceTests
{
    [Fact]
    public async Task A_new_template_is_inactive_and_does_not_replace_the_active_one()
    {
        var fixture = new Fixture();
        var active = fixture.Add(NotificationType.Welcome, "Hello {{EmployeeName}}", "<p>Dear {{EmployeeName}}</p>", active: true);

        var created = await fixture.Service.CreateAsync(
            new CreateNotificationTemplateRequest(NotificationType.Welcome, " New subject ", "<p>New body</p>"));

        Assert.False(created.IsActive);
        Assert.Equal("New subject", created.Subject);                     // trimmed like the entity stores it
        Assert.Equal(NotificationType.Welcome, created.Type);
        Assert.Equal(["EmployeeName"], created.AllowedPlaceholders);
        Assert.True(active.IsActive);
        Assert.Equal(2, fixture.Repository.Items.Count);
    }

    [Theory]
    [InlineData("Subject {{Nope}}", "<p>Body</p>")]
    [InlineData("Subject", "<p>{{BookTitle}}</p>")]        // BookTitle has no value for Welcome
    public async Task A_placeholder_without_a_value_for_the_type_is_refused_with_its_name(string subject, string body)
    {
        var fixture = new Fixture();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            fixture.Service.CreateAsync(new CreateNotificationTemplateRequest(NotificationType.Welcome, subject, body)));

        Assert.Equal(NotificationErrors.TemplateUnknownPlaceholder, ex.Error);
        var names = Assert.IsAssignableFrom<IEnumerable<string>>(ex.Extensions["placeholders"]).ToList();
        Assert.Single(names);
        Assert.Empty(fixture.Repository.Items);
    }

    [Theory]
    [InlineData("Subject {{Book.Title}}", "<p>Body</p>")]
    [InlineData("Subject", "<p>{{EmployeeName</p>")]
    public async Task A_malformed_placeholder_is_refused(string subject, string body)
    {
        var fixture = new Fixture();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            fixture.Service.CreateAsync(new CreateNotificationTemplateRequest(NotificationType.Welcome, subject, body)));

        Assert.Equal(NotificationErrors.TemplateMalformedPlaceholder, ex.Error);
    }

    [Fact]
    public async Task A_template_may_use_any_subset_of_the_placeholders_of_its_type()
    {
        var fixture = new Fixture();

        var created = await fixture.Service.CreateAsync(
            new CreateNotificationTemplateRequest(NotificationType.ReservationReady, "Ready", "<p>{{ BookTitle }} until {{ReadyUntil}}</p>"));

        Assert.Equal(["EmployeeName", "BookTitle", "ReadyUntil"], created.AllowedPlaceholders);
    }

    [Fact]
    public async Task Update_changes_subject_and_body_checks_the_type_of_the_stored_template_and_keeps_the_active_flag()
    {
        var fixture = new Fixture();
        var template = fixture.Add(NotificationType.PenaltyApplied, "Old", "<p>Old</p>", active: true);

        var updated = await fixture.Service.UpdateAsync(
            template.Id, new UpdateNotificationTemplateRequest("Penalty {{Points}}", "<p>{{TotalPoints}} - {{Reason}}</p>"));

        Assert.Equal("Penalty {{Points}}", updated.Subject);
        Assert.True(updated.IsActive);
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            fixture.Service.UpdateAsync(template.Id, new UpdateNotificationTemplateRequest("Subject", "<p>{{DueDate}}</p>")));
        Assert.Equal(NotificationErrors.TemplateUnknownPlaceholder, ex.Error);
        Assert.Equal("Penalty {{Points}}", template.Subject);                 // the refused text was not applied
    }

    [Fact]
    public async Task Activate_makes_the_template_the_only_active_one_of_its_type_and_leaves_other_types_alone()
    {
        var fixture = new Fixture();
        var oldActive = fixture.Add(NotificationType.Welcome, "Old", "<p>Old</p>", active: true);
        var candidate = fixture.Add(NotificationType.Welcome, "New", "<p>New</p>", active: false);
        var otherType = fixture.Add(NotificationType.PasswordChanged, "Security", "<p>Hi</p>", active: true);

        var result = await fixture.Service.ActivateAsync(candidate.Id);

        Assert.True(result.IsActive);
        Assert.True(candidate.IsActive);
        Assert.False(oldActive.IsActive);
        Assert.True(otherType.IsActive);
        Assert.Equal(1, fixture.Repository.Items.Count(t => t.Type == NotificationType.Welcome && t.IsActive));
    }

    [Fact]
    public async Task Activating_the_active_template_again_changes_nothing()
    {
        var fixture = new Fixture();
        var active = fixture.Add(NotificationType.Welcome, "S", "<p>B</p>", active: true);

        var result = await fixture.Service.ActivateAsync(active.Id);

        Assert.True(result.IsActive);
        Assert.Equal(0, fixture.Repository.DeactivateCalls);
    }

    [Fact]
    public async Task An_inactive_template_is_deleted_but_the_active_one_is_refused()
    {
        var fixture = new Fixture();
        var active = fixture.Add(NotificationType.Welcome, "S", "<p>B</p>", active: true);
        var draft = fixture.Add(NotificationType.Welcome, "D", "<p>D</p>", active: false);

        await fixture.Service.DeleteAsync(draft.Id);
        var ex = await Assert.ThrowsAsync<DomainException>(() => fixture.Service.DeleteAsync(active.Id));

        Assert.Equal(NotificationErrors.TemplateActiveCannotBeDeleted, ex.Error);
        Assert.Equal([active], fixture.Repository.Items);
    }

    [Fact]
    public async Task An_unknown_id_is_reported_for_get_update_activate_and_delete()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();

        foreach (var call in new Func<Task>[]
                 {
                     () => fixture.Service.GetAsync(id),
                     () => fixture.Service.UpdateAsync(id, new UpdateNotificationTemplateRequest("S", "<p>B</p>")),
                     () => fixture.Service.ActivateAsync(id),
                     () => fixture.Service.DeleteAsync(id),
                 })
        {
            var ex = await Assert.ThrowsAsync<DomainException>(call);
            Assert.Equal(NotificationErrors.TemplateDoesNotExist, ex.Error);
        }
    }

    [Fact]
    public async Task List_filters_by_type()
    {
        var fixture = new Fixture();
        fixture.Add(NotificationType.Welcome, "W", "<p>W</p>", active: true);
        fixture.Add(NotificationType.ReturnReceipt, "R", "<p>R</p>", active: true);

        Assert.Equal(2, (await fixture.Service.ListAsync(null)).Count);
        var welcome = Assert.Single(await fixture.Service.ListAsync(NotificationType.Welcome));
        Assert.Equal("W", welcome.Subject);
    }

    [Fact]
    public void Preview_renders_the_text_with_example_values_in_the_email_layout_and_stores_nothing()
    {
        var fixture = new Fixture();

        var preview = fixture.Service.Preview(new PreviewNotificationTemplateRequest(
            NotificationType.LoanConfirmation, "Your loan of \"{{BookTitle}}\"", "<p>Dear {{EmployeeName}}, return by {{DueDate}}.</p>"));

        Assert.Equal("Your loan of \"Clean Code\"", preview.Subject);
        Assert.Contains("Dear Anna Petrosyan, return by 13.11.2026.", preview.HtmlBody);
        Assert.StartsWith("<!DOCTYPE html>", preview.HtmlBody);
        Assert.Empty(fixture.Repository.Items);
    }

    [Fact]
    public void Preview_applies_the_same_placeholder_rules_as_saving()
    {
        var fixture = new Fixture();

        var unknown = Assert.Throws<DomainException>(() => fixture.Service.Preview(
            new PreviewNotificationTemplateRequest(NotificationType.Welcome, "S", "<p>{{DueDate}}</p>")));
        var malformed = Assert.Throws<DomainException>(() => fixture.Service.Preview(
            new PreviewNotificationTemplateRequest(NotificationType.Welcome, "S", "<p>{{EmployeeName</p>")));

        Assert.Equal(NotificationErrors.TemplateUnknownPlaceholder, unknown.Error);
        Assert.Equal(NotificationErrors.TemplateMalformedPlaceholder, malformed.Error);
    }

    [Fact]
    public void Every_notification_type_has_allowed_placeholders_and_an_example_for_each_of_them()
    {
        foreach (var type in Enum.GetValues<NotificationType>())
        {
            var allowed = NotificationPlaceholders.AllowedFor(type);
            Assert.Contains(NotificationPlaceholders.EmployeeName, allowed);   // the creator always adds it
            Assert.All(allowed, name => Assert.True(NotificationPlaceholders.SampleValues.ContainsKey(name), $"{type}: no example for {name}"));
        }
    }

    [Theory]
    [InlineData(null, "S", "B", "Type")]
    [InlineData(99, "S", "B", "Type")]
    [InlineData(1, "  ", "B", "Subject")]
    [InlineData(1, null, "B", "Subject")]
    [InlineData(1, "S", " ", "BodyTemplate")]
    public void The_create_request_validator_names_the_field(int? type, string? subject, string? body, string field)
    {
        var result = new CreateNotificationTemplateRequestValidator().Validate(
            new CreateNotificationTemplateRequest((NotificationType?)type, subject, body));

        Assert.Contains(result.Errors, e => e.PropertyName == field);
    }

    [Fact]
    public void A_subject_longer_than_300_characters_after_trimming_is_invalid_but_300_is_fine()
    {
        var validator = new UpdateNotificationTemplateRequestValidator();

        Assert.True(validator.Validate(new UpdateNotificationTemplateRequest(" " + new string('a', 300) + " ", "B")).IsValid);
        Assert.False(validator.Validate(new UpdateNotificationTemplateRequest(new string('a', 301), "B")).IsValid);
    }

    private sealed class Fixture
    {
        public FakeRepository Repository { get; } = new();
        public NotificationTemplateService Service { get; }

        public Fixture() =>
            Service = new NotificationTemplateService(Repository, new NotificationTemplateRenderer(), new PassThroughUnitOfWork(), new SequentialIds());

        public NotificationTemplate Add(NotificationType type, string subject, string body, bool active)
        {
            var template = new NotificationTemplate(Guid.NewGuid(), type, subject, body, active);
            Repository.Items.Add(template);
            return template;
        }
    }

    private sealed class SequentialIds : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class PassThroughUnitOfWork : IUnitOfWork
    {
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);
    }

    private sealed class FakeRepository : INotificationTemplateRepository
    {
        public List<NotificationTemplate> Items { get; } = [];
        public int DeactivateCalls { get; private set; }

        public Task<NotificationTemplate?> FindActiveAsync(NotificationType type, CancellationToken ct = default) =>
            Task.FromResult(Items.SingleOrDefault(t => t.Type == type && t.IsActive));

        public Task<IReadOnlyList<NotificationTemplate>> ListAsync(NotificationType? type, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotificationTemplate>>(Items.Where(t => type is null || t.Type == type).ToList());

        public Task<NotificationTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Items.SingleOrDefault(t => t.Id == id));

        public void Add(NotificationTemplate template) => Items.Add(template);

        public void Remove(NotificationTemplate template) => Items.Remove(template);

        public Task DeactivateOthersAsync(NotificationType type, Guid exceptId, CancellationToken ct = default)
        {
            DeactivateCalls++;
            foreach (var template in Items.Where(t => t.Type == type && t.Id != exceptId))
                template.Deactivate();
            return Task.CompletedTask;
        }
    }
}
