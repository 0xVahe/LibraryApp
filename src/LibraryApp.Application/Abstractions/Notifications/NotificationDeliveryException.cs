namespace LibraryApp.Application.Abstractions.Notifications;

/// <summary>The mail server did not accept a message. The text has no addresses (NFR-06), so it can go to logs and LastError.</summary>
public sealed class NotificationDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);
