using System.Reflection;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Domain.Tests.Notifications;

/// <summary>
/// The numbers are stored in the database (SD-11): if one changes, existing rows change meaning.
/// </summary>
public sealed class NotificationEnumValuesTests
{
    [Theory]
    [InlineData(NotificationType.Welcome, 1)]
    [InlineData(NotificationType.LoanConfirmation, 2)]
    [InlineData(NotificationType.DueSoonReminder, 3)]
    [InlineData(NotificationType.OverdueNotice, 4)]
    [InlineData(NotificationType.ReservationReady, 5)]
    [InlineData(NotificationType.PenaltyApplied, 6)]
    [InlineData(NotificationType.AccessRestricted, 7)]
    [InlineData(NotificationType.AccessSuspended, 8)]
    [InlineData(NotificationType.DueDateCalendarItem, 9)]
    [InlineData(NotificationType.ReturnReceipt, 10)]
    [InlineData(NotificationType.PasswordChanged, 11)]
    public void NotificationType_HasTheAgreedNumber(NotificationType type, int number)
    {
        Assert.Equal(number, (int)type);
    }

    [Theory]
    [InlineData(NotificationChannel.Outlook, 1)]
    [InlineData(NotificationChannel.InApp, 2)]
    public void NotificationChannel_HasTheAgreedNumber(NotificationChannel channel, int number)
    {
        Assert.Equal(number, (int)channel);
    }

    [Theory]
    [InlineData(NotificationStatus.Pending, 1)]
    [InlineData(NotificationStatus.Sent, 2)]
    [InlineData(NotificationStatus.Failed, 3)]
    public void NotificationStatus_HasTheAgreedNumber(NotificationStatus status, int number)
    {
        Assert.Equal(number, (int)status);
    }

    [Fact]
    public void EveryNotificationErrorCode_StartsWithTheModulePrefix()
    {
        var errors = typeof(NotificationErrors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Error))
            .Select(f => (Error)f.GetValue(null)!)
            .ToList();

        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.StartsWith("notifications.", error.Code));
    }
}
