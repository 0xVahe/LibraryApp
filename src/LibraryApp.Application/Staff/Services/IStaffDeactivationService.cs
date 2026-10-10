using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Services;

/// <summary>Administrative deactivation (MEM-01, BR-MEM-4). Request validation runs at the API boundary.</summary>
public interface IStaffDeactivationService
{
    Task DeactivateAsync(Guid id, DeactivateStaffMemberRequest request, CancellationToken ct = default);
}
