using LibraryApp.Application.Staff.Dto;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Ports;

public interface IStaffCategoryRepository
{
    Task<IReadOnlyList<StaffCategoryDto>> GetListAsync(CancellationToken ct = default);

    Task<StaffCategory?> GetByIdAsync(Guid id, CancellationToken ct = default);

    void Add(StaffCategory category);
}