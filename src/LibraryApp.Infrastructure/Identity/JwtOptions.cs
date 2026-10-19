using System.ComponentModel.DataAnnotations;

namespace LibraryApp.Infrastructure.Identity;

/// <summary>Settings section "Jwt". Checked at start (ValidateOnStart).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 key, 32+ characters. Never committed (NFR-04): user-secrets locally, Jwt__SigningKey elsewhere.</summary>
    [Required(ErrorMessage = "Jwt:SigningKey is missing. Set it with dotnet user-secrets or the Jwt__SigningKey environment variable.")]
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters.")]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 60;

    /// <summary>Maximum delay before an account change invalidates a previously accepted token.</summary>
    [Range(1, 30)]
    public int SecurityStampCacheSeconds { get; set; }
}
