using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Tests.Staff;

namespace LibraryApp.Domain.Tests.Notifications;

public sealed class NotificationTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly LocalDate = new(2026, 10, 6);

    private static Notification Create(
        NotificationChannel channel = NotificationChannel.Outlook,
        Guid? staffMemberId = null,
        string recipientEmail = "anna.petrosyan@ameriabank.am",
        string? ccEmail = null,
        string subject = "Your book is due soon",
        string body = "<p>Please return the book.</p>",
        DateTime? createdAtUtc = null,
        Guid? loanId = null,
        Guid? eventId = null,
        string? calendarItem = null)
    {
        return Notification.Create(
            id: Guid.NewGuid(),
            staffMemberId: staffMemberId ?? Guid.NewGuid(),
            type: NotificationType.DueSoonReminder,
            channel: channel,
            recipientEmail: recipientEmail,
            ccEmail: ccEmail,
            subject: subject,
            body: body,
            createdAtUtc: createdAtUtc ?? CreatedAtUtc,
            localDate: LocalDate,
            loanId: loanId,
            eventId: eventId,
            calendarItem: calendarItem);
    }

    // ---------- Create ----------

    [Fact]
    public void Create_keeps_the_calendar_item_for_the_email_attachment()
    {
        var notification = Create(calendarItem: "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n");

        Assert.Equal("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n", notification.CalendarItem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_without_a_calendar_item_has_none(string? calendarItem)
    {
        Assert.Null(Create(calendarItem: calendarItem).CalendarItem);
    }

    [Fact]
    public void Create_makes_a_pending_notification_with_no_attempts()
    {
        Guid loanId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();

        Notification notification = Create(loanId: loanId, eventId: eventId);

        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal(0, notification.Attempts);
        Assert.Equal(loanId, notification.LoanId);
        Assert.Equal(eventId, notification.EventId);
        Assert.Equal(CreatedAtUtc, notification.CreatedAtUtc);
        Assert.Equal(LocalDate, notification.LocalDate);
        Assert.Null(notification.SentAtUtc);
        Assert.Null(notification.ReadAtUtc);
        Assert.Null(notification.NextAttemptAtUtc);
        Assert.Null(notification.LastError);
    }

    [Fact]
    public void Create_trims_texts_and_stores_an_empty_cc_as_null()
    {
        Notification notification = Create(
            recipientEmail: "  anna.petrosyan@ameriabank.am  ",
            ccEmail: "   ",
            subject: "  Your book is due soon  ");

        Assert.Equal("anna.petrosyan@ameriabank.am", notification.RecipientEmail);
        Assert.Null(notification.CcEmail);
        Assert.Equal("Your book is due soon", notification.Subject);
    }

    [Fact]
    public void Create_keeps_the_line_manager_in_cc()
    {
        Notification notification = Create(ccEmail: "manager@ameriabank.am");

        Assert.Equal("manager@ameriabank.am", notification.CcEmail);
    }

    [Fact]
    public void Create_without_loan_and_event_is_allowed()
    {
        Notification notification = Create();

        Assert.Null(notification.LoanId);
        Assert.Null(notification.EventId);
    }

    [Fact]
    public void Create_rejects_an_empty_staff_member_id()
    {
        DomainAssert.ThrowsError(
            NotificationErrors.InvalidReferenceId,
            () => Create(staffMemberId: Guid.Empty));
    }

    [Fact]
    public void Create_rejects_an_empty_loan_id()
    {
        DomainAssert.ThrowsError(
            NotificationErrors.InvalidReferenceId,
            () => Create(loanId: Guid.Empty));
    }

    [Fact]
    public void Create_rejects_an_empty_event_id()
    {
        DomainAssert.ThrowsError(
            NotificationErrors.InvalidReferenceId,
            () => Create(eventId: Guid.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_recipient(string recipientEmail)
    {
        DomainAssert.ThrowsError(
            NotificationErrors.RecipientRequired,
            () => Create(recipientEmail: recipientEmail));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_subject(string subject)
    {
        DomainAssert.ThrowsError(
            NotificationErrors.SubjectRequired,
            () => Create(subject: subject));
    }

    [Fact]
    public void Create_accepts_a_subject_of_exactly_300_characters()
    {
        Notification notification = Create(subject: new string('a', Notification.SubjectMaxLength));

        Assert.Equal(Notification.SubjectMaxLength, notification.Subject.Length);
    }

    [Fact]
    public void Create_rejects_a_subject_longer_than_300_characters()
    {
        DomainAssert.ThrowsError(
            NotificationErrors.SubjectTooLong,
            () => Create(subject: new string('a', Notification.SubjectMaxLength + 1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_body(string body)
    {
        DomainAssert.ThrowsError(
            NotificationErrors.BodyRequired,
            () => Create(body: body));
    }

    [Fact]
    public void Create_rejects_a_time_that_is_not_utc()
    {
        DomainAssert.ThrowsError(
            NotificationErrors.TimestampMustBeUtc,
            () => Create(createdAtUtc: new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Local)));
    }

    // ---------- MarkSent ----------

    [Fact]
    public void MarkSent_sets_status_and_time()
    {
        Notification notification = Create();
        DateTime sentAtUtc = CreatedAtUtc.AddSeconds(2);

        notification.MarkSent(sentAtUtc);

        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(sentAtUtc, notification.SentAtUtc);
    }

    [Fact]
    public void MarkSent_after_a_failure_clears_the_retry_state()
    {
        Notification notification = Create();
        notification.MarkFailed("SMTP timeout", CreatedAtUtc.AddMinutes(5));

        notification.MarkSent(CreatedAtUtc.AddMinutes(5));

        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(1, notification.Attempts);
        Assert.Null(notification.NextAttemptAtUtc);
        Assert.Null(notification.LastError);
    }

    [Fact]
    public void MarkSent_twice_is_rejected()
    {
        Notification notification = Create();
        notification.MarkSent(CreatedAtUtc);

        DomainAssert.ThrowsError(
            NotificationErrors.AlreadySent,
            () => notification.MarkSent(CreatedAtUtc.AddMinutes(1)));
    }

    [Fact]
    public void MarkSent_rejects_a_time_that_is_not_utc()
    {
        Notification notification = Create();

        DomainAssert.ThrowsError(
            NotificationErrors.TimestampMustBeUtc,
            () => notification.MarkSent(new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Unspecified)));
    }

    // ---------- MarkFailed (NOT-08) ----------

    [Fact]
    public void MarkFailed_counts_the_attempt_and_schedules_the_retry()
    {
        Notification notification = Create();
        DateTime retryAtUtc = CreatedAtUtc.AddMinutes(5);

        notification.MarkFailed("SMTP timeout", retryAtUtc);

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(1, notification.Attempts);
        Assert.Equal(retryAtUtc, notification.NextAttemptAtUtc);
        Assert.Equal("SMTP timeout", notification.LastError);
    }

    [Fact]
    public void MarkFailed_without_a_next_attempt_means_no_more_retries()
    {
        Notification notification = Create();
        notification.MarkFailed("first", CreatedAtUtc.AddMinutes(5));
        notification.MarkFailed("second", CreatedAtUtc.AddMinutes(20));
        notification.MarkFailed("third", CreatedAtUtc.AddMinutes(80));

        notification.MarkFailed("fourth", null);

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(4, notification.Attempts);
        Assert.Null(notification.NextAttemptAtUtc);
        Assert.Equal("fourth", notification.LastError);
    }

    [Fact]
    public void MarkFailed_cuts_a_very_long_error_message()
    {
        Notification notification = Create();

        notification.MarkFailed(new string('x', Notification.LastErrorMaxLength + 500), null);

        Assert.Equal(Notification.LastErrorMaxLength, notification.LastError!.Length);
    }

    [Fact]
    public void MarkFailed_after_sent_is_rejected()
    {
        Notification notification = Create();
        notification.MarkSent(CreatedAtUtc);

        DomainAssert.ThrowsError(
            NotificationErrors.AlreadySent,
            () => notification.MarkFailed("late error", null));
    }

    [Fact]
    public void MarkFailed_rejects_a_retry_time_that_is_not_utc()
    {
        Notification notification = Create();

        DomainAssert.ThrowsError(
            NotificationErrors.TimestampMustBeUtc,
            () => notification.MarkFailed("SMTP timeout", new DateTime(2026, 10, 6, 9, 5, 0, DateTimeKind.Local)));
    }

    // ---------- MarkRead (NOT-04) ----------

    [Fact]
    public void MarkRead_sets_the_read_time_of_an_in_app_notification()
    {
        Notification notification = Create(channel: NotificationChannel.InApp);
        DateTime readAtUtc = CreatedAtUtc.AddHours(1);

        notification.MarkRead(readAtUtc);

        Assert.Equal(readAtUtc, notification.ReadAtUtc);
    }

    [Fact]
    public void MarkRead_twice_keeps_the_first_read_time()
    {
        Notification notification = Create(channel: NotificationChannel.InApp);
        DateTime firstReadAtUtc = CreatedAtUtc.AddHours(1);

        notification.MarkRead(firstReadAtUtc);
        notification.MarkRead(firstReadAtUtc.AddHours(1));

        Assert.Equal(firstReadAtUtc, notification.ReadAtUtc);
    }

    [Fact]
    public void MarkRead_rejects_an_outlook_notification()
    {
        Notification notification = Create(channel: NotificationChannel.Outlook);

        DomainAssert.ThrowsError(
            NotificationErrors.OnlyInAppCanBeRead,
            () => notification.MarkRead(CreatedAtUtc.AddHours(1)));
    }

    [Fact]
    public void MarkRead_rejects_a_time_that_is_not_utc()
    {
        Notification notification = Create(channel: NotificationChannel.InApp);

        DomainAssert.ThrowsError(
            NotificationErrors.TimestampMustBeUtc,
            () => notification.MarkRead(new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Local)));
    }
}
