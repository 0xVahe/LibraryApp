using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Ports;

public interface IStaffMemberRepository
{
    /// <summary>Tracked: changes are saved by the unit of work.</summary>
    Task<StaffMember?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<StaffMemberStatus?> GetStatusAsync(Guid id, CancellationToken ct);

    Task<StaffLendingInfo?> GetLendingInfoAsync(Guid id, CancellationToken ct);
    
    /// <summary>"First Last" by id, in one query; ids that do not exist are missing from the result.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    
    /// <summary>Exact match on the stored employee ID; null when there is none.</summary>
    Task<StaffMemberLookup?> FindByEmployeeIdAsync(string employeeId, CancellationToken ct);

    /// <summary>The email must already be in lower case, as it is stored.</summary>
    Task<StaffMemberLookup?> FindByCorporateEmailAsync(string corporateEmail, CancellationToken ct);
    
    /// <summary>Contacts by id, in one query; ids that do not exist are missing from the result.</summary>
    Task<IReadOnlyDictionary<Guid, StaffContact>> GetContactsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

/// <summary>Status of a staff member and the limits of their category, read in one query.</summary>
public sealed record StaffLendingInfo(StaffMemberStatus Status, LendingRules CategoryRules);