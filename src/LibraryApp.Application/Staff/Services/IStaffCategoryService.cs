using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Services;

public interface IStaffCategoryService
{
    Task<IReadOnlyList<StaffCategoryDto>> GetListAsync(
        CancellationToken ct = default);

    Task<StaffCategoryDto> GetAsync(
        Guid id,
        CancellationToken ct = default);

    Task<StaffCategoryDto> CreateAsync(
        UpsertStaffCategoryRequest request,
        CancellationToken ct = default);

    Task<StaffCategoryDto> UpdateAsync(
        Guid id,
        UpsertStaffCategoryRequest request,
        CancellationToken ct = default);
}