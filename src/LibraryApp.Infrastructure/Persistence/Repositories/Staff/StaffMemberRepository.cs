using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Staff;

public sealed class StaffMemberRepository(LibraryDbContext db) : IStaffMemberRepository
{
    public Task<StaffMember?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.StaffMembers.SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<StaffMemberStatus?> GetStatusAsync(Guid id, CancellationToken ct) =>
        db.StaffMembers
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => (StaffMemberStatus?)x.Status)
            .SingleOrDefaultAsync(ct);

    public Task<StaffLendingInfo?> GetLendingInfoAsync(Guid id, CancellationToken ct) =>
        (from member in db.StaffMembers.AsNoTracking()
            join category in db.StaffCategories.AsNoTracking() on member.StaffCategoryId equals category.Id
            where member.Id == id
            select new StaffLendingInfo(
                member.Status,
                new LendingRules(category.MaxLoans, category.LoanDays, category.MaxRenewals)))
        .SingleOrDefaultAsync(ct);
    
    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.StaffMembers
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, Name = x.FirstName + " " + x.LastName })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
    
    // SQL Server's default collation ignores case, so "emp-10001" finds "EMP-10001"; both columns have a unique index.
    public Task<StaffMemberLookup?> FindByEmployeeIdAsync(string employeeId, CancellationToken ct) =>
        Lookups().SingleOrDefaultAsync(x => x.EmployeeId == employeeId, ct);

    public Task<StaffMemberLookup?> FindByCorporateEmailAsync(string corporateEmail, CancellationToken ct) =>
        Lookups().SingleOrDefaultAsync(x => x.CorporateEmail == corporateEmail, ct);

    private IQueryable<StaffMemberLookup> Lookups() =>
        db.StaffMembers
            .AsNoTracking()
            .Select(x => new StaffMemberLookup(
                x.Id, x.EmployeeId, x.FirstName + " " + x.LastName, x.CorporateEmail, x.Status));
}