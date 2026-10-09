using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Staff;

public sealed class StaffManagementRepository(LibraryDbContext db) : IStaffManagementRepository
{
    public Task<StaffMember?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.StaffMembers.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<StaffMemberDto?> GetDetailsAsync(Guid id, CancellationToken ct)
    {
        var profile = await (from member in db.StaffMembers.AsNoTracking()
            join department in db.Departments on member.DepartmentId equals department.Id
            join category in db.StaffCategories on member.StaffCategoryId equals category.Id
            where member.Id == id
            select new
            {
                member.UserId,
                Dto = new StaffMemberDto(member.Id, member.EmployeeId, member.FirstName, member.LastName,
                 member.CorporateEmail, member.DepartmentId, department.Name, member.Position, member.LineManagerEmail,
                 member.StaffCategoryId, category.Name, member.Status.ToString(), member.JoinedLibraryAtUtc, string.Empty)
            }).SingleOrDefaultAsync(ct);

        if (profile is null)
            return null;

        return profile.Dto with { Role = await StaffAccountRoles.GetAsync(db, profile.UserId, ct) };
    }

    public Task<bool> EmployeeIdExistsAsync(string employeeId, CancellationToken ct) =>
        db.StaffMembers.AnyAsync(x => x.EmployeeId == employeeId, ct);

    public Task<bool> EmailExistsAsync(string email, Guid? exceptMemberId, CancellationToken ct) =>
        db.StaffMembers.AnyAsync(x => x.CorporateEmail == email && (!exceptMemberId.HasValue || x.Id != exceptMemberId.Value), ct);

    public Task<string?> GetDepartmentNameAsync(Guid id, CancellationToken ct) =>
        db.Departments.Where(x => x.Id == id).Select(x => x.Name).SingleOrDefaultAsync(ct);

    public Task<string?> GetCategoryNameAsync(Guid id, CancellationToken ct) =>
        db.StaffCategories.Where(x => x.Id == id).Select(x => x.Name).SingleOrDefaultAsync(ct);

    public void Add(StaffMember member) => db.StaffMembers.Add(member);
}
