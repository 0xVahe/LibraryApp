using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Staff.Services;

/// <summary>MEM-03 for login and enrolment: only the exact corporate domain passes (no subdomains or look-alikes).</summary>
public sealed class CorporateEmailPolicy(IOptions<CorporateEmailOptions> options)
{
    private readonly string _domain = options.Value.Domain.Trim().TrimStart('@');

    public bool IsCorporate(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var trimmed = email.Trim();
        var at = trimmed.LastIndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1)
            return false;

        return string.Equals(trimmed[(at + 1)..], _domain, StringComparison.OrdinalIgnoreCase);
    }
}