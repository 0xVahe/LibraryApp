using System.ComponentModel.DataAnnotations;
using MailKit.Security;

namespace LibraryApp.Infrastructure.Mail;

/// <summary>Settings section "Mail". The defaults point to smtp4dev on this machine (NOT-03). Checked at start.</summary>
public sealed class MailOptions : IValidatableObject
{
    public const string SectionName = "Mail";

    [Required]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 2525;

    /// <summary>None for smtp4dev; StartTls or SslOnConnect for a real server.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.None;

    [Required, EmailAddress]
    public string FromAddress { get; set; } = "library@ameriabank.am";

    [Required]
    public string FromName { get; set; } = "Ameriabank Library";

    /// <summary>Empty for smtp4dev. Never committed (NFR-04): user-secrets or Mail__UserName / Mail__Password.</summary>
    public string? UserName { get; set; }

    public string? Password { get; set; }

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;

    // Both or neither: half of the credentials would fail only on the first email. No values in the message (NFR-04).
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrEmpty(UserName) != string.IsNullOrEmpty(Password))
            yield return new ValidationResult(
                "Mail:UserName and Mail:Password must be set together or both left empty.",
                [nameof(UserName), nameof(Password)]);
    }
}
