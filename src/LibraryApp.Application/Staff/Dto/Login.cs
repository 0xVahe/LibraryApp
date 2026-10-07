using System.ComponentModel.DataAnnotations;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Staff.Dto;

/// <summary>Credentials for POST /api/auth/login.</summary>
public sealed record LoginRequest(string? Email, string? Password);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    string Email,
    IReadOnlyList<string> Roles);