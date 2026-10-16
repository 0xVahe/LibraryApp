namespace LibraryApp.Application.Staff.Dto;

/// <summary>Credentials for POST /api/auth/login.</summary>
public sealed record LoginRequest(string? Email, string? Password);

/// <summary>Body of POST /api/auth/change-password.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    string Email,
    IReadOnlyList<string> Roles,
    bool PasswordChangeRequired = false);
