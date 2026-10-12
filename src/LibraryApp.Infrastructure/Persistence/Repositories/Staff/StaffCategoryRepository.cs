using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Staff;

public sealed class StaffCategoryRepository(LibraryDbContext db) : IStaffCategoryRepository
{
    public async Task<IReadOnlyList<StaffCategoryDto>> GetListAsync(CancellationToken ct = default)
    {
        return await db.StaffCategories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new StaffCategoryDto(
                category.Id,
                category.Name,
                category.MaxLoans,
                category.LoanDays,
                category.MaxRenewals))
            .ToListAsync(ct);
    }

    public Task<bool> NameExistsAsync(string name, Guid? exceptId = null, CancellationToken ct = default)
    {
        return db.StaffCategories.AnyAsync(
            category => category.Name == name && (!exceptId.HasValue || category.Id != exceptId.Value), ct);
    }

    public Task<StaffCategory?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return db.StaffCategories.
            SingleOrDefaultAsync(category => category.Id == id, ct);
    }

    public void Add(StaffCategory category)
    {
        db.StaffCategories.Add(category);
    }
}
