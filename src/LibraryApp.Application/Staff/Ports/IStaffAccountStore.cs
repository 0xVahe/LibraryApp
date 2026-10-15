namespace LibraryApp.Application.Staff.Ports;

/// <summary>Account writes use Identity in Infrastructure, inside the caller's unit of work.</summary>
public interface IStaffAccountStore
{
    Task<bool> EmailExistsAsync(string email, Guid? exceptUserId, CancellationToken ct);
    Task<IReadOnlyCollection<string>> GetExistingEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken ct);
    Task CreateAsync(Guid userId, string email, string password, string role, CancellationToken ct,
        bool requirePasswordChange = false);
    Task UpdateEmailAsync(Guid userId, string email, CancellationToken ct);
    Task DeactivateAsync(Guid userId, CancellationToken ct);
    Task<string> GetRoleAsync(Guid userId, CancellationToken ct);
}
