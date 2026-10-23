using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using LibraryApp.Application.Common;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Staff;

public sealed class StaffManagementRepository(LibraryDbContext db) : IStaffManagementRepository
{
    public Task<StaffMember?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.StaffMembers.SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<StaffMemberProfile?> GetForProfileUpdateAsync(Guid id, CancellationToken ct) =>
        (from member in db.StaffMembers
            join department in db.Departments on member.DepartmentId equals department.Id
            join category in db.StaffCategories on member.StaffCategoryId equals category.Id
            where member.Id == id
            select new StaffMemberProfile(member, department.Name, category.Name))
        .SingleOrDefaultAsync(ct);

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

    public async Task<IReadOnlyList<Department>> GetDepartmentsByNamesAsync(IReadOnlyCollection<string> names,
        CancellationToken ct) => await db.Departments.AsNoTracking().Where(x => names.Contains(x.Name)).ToListAsync(ct);

    public async Task<IReadOnlyList<StaffCategory>> GetCategoriesByNamesAsync(IReadOnlyCollection<string> names,
        CancellationToken ct) => await db.StaffCategories.AsNoTracking().Where(x => names.Contains(x.Name)).ToListAsync(ct);

    public async Task<IReadOnlyCollection<string>> GetExistingEmployeeIdsAsync(IReadOnlyCollection<string> employeeIds,
        CancellationToken ct) => await db.StaffMembers.Where(x => employeeIds.Contains(x.EmployeeId))
        .Select(x => x.EmployeeId).ToListAsync(ct);

    public async Task<IReadOnlyCollection<string>> GetExistingEmailsAsync(IReadOnlyCollection<string> emails,
        CancellationToken ct) => await db.StaffMembers.Where(x => emails.Contains(x.CorporateEmail))
        .Select(x => x.CorporateEmail).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDepartmentNamesAsync(IReadOnlyCollection<Guid> ids,
        CancellationToken ct) => await db.Departments.Where(x => ids.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetCategoryNamesAsync(IReadOnlyCollection<Guid> ids,
        CancellationToken ct) => await db.StaffCategories.Where(x => ids.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    public async Task<PagedResult<StaffMemberListItemDto>> SearchAsync(
        StaffMemberSearchFilter filter,
        PageRequest paging,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(paging);

        IQueryable<StaffMember> members = db.StaffMembers.AsNoTracking();

        if (filter.Text is not null)
        {
            string text = filter.Text;

            members = members.Where(member =>
                member.FirstName.StartsWith(text) ||
                member.LastName.StartsWith(text) ||
                (member.FirstName + " " + member.LastName).StartsWith(text) ||
                member.EmployeeId.StartsWith(text));
        }

        if (filter.DepartmentId is Guid departmentId)
        {
            members = members.Where(member => member.DepartmentId == departmentId);
        }

        if (filter.Status is StaffMemberStatus status)
        {
            members = members.Where(member => member.Status == status);
        }

        int totalCount = await members.CountAsync(ct);

        List<StaffMemberListItemDto> items = await (
            from member in members
            join department in db.Departments.AsNoTracking()
                on member.DepartmentId equals department.Id into departmentRows
            from department in departmentRows.DefaultIfEmpty()
            orderby member.LastName, member.FirstName, member.Id
            select new StaffMemberListItemDto(
                member.Id,
                member.EmployeeId,
                member.FirstName,
                member.LastName,
                member.CorporateEmail,
                member.DepartmentId,
                department == null ? string.Empty : department.Name,
                member.Status.ToString()))
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(ct);

        return new PagedResult<StaffMemberListItemDto>(
            items,
            totalCount,
            paging.Page,
            paging.PageSize);
    }

    public void Add(StaffMember member) => db.StaffMembers.Add(member);
}
