using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Staff;

public sealed class DepartmentRepository(LibraryDbContext db) : IDepartmentRepository
{
    public async Task<IReadOnlyList<DepartmentDto>> ListAsync(CancellationToken ct) =>
        await db.Departments.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new DepartmentDto(x.Id, x.Name)).ToListAsync(ct);

    public async Task<IReadOnlyList<DepartmentSettingsDto>> ListForSettingsAsync(CancellationToken ct) =>
        await db.Departments.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new DepartmentSettingsDto(
                x.Id,
                x.Name,
                db.StaffMembers.Count(member => member.DepartmentId == x.Id)))
            .ToListAsync(ct);

    public Task<Department?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Departments.SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct) =>
        db.Departments.AnyAsync(x => x.Name == name && (!exceptId.HasValue || x.Id != exceptId.Value), ct);

    public void Add(Department department) => db.Departments.Add(department);
}
