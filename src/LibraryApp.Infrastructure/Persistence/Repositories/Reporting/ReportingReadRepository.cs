using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Ports;
using LibraryApp.Application.Reporting.Options;
using Microsoft.Extensions.Options;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Reporting;

public sealed class ReportingReadRepository(
    LibraryDbContext db,
    IOptions<ReportingOptions> options) : IReportingReadRepository
{
    public async Task<IReadOnlyList<OverdueLoanData>> GetOverdueLoansAsync(
        DateTime overdueCutoffUtc,
	Guid? departmentId,
        CancellationToken ct = default)
    {
        return await (
                from loan in db.Loans.AsNoTracking()
                where (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue)
                      && loan.DueAtUtc < overdueCutoffUtc
                join staff in db.StaffMembers.AsNoTracking()
                    on loan.StaffMemberId equals staff.Id
		where !departmentId.HasValue || staff.DepartmentId == departmentId.Value
                join department in db.Departments.AsNoTracking()
                    on staff.DepartmentId equals department.Id
                join copy in db.BookCopies.AsNoTracking()
                    on loan.BookCopyId equals copy.Id
                join book in db.Books.AsNoTracking()
                    on copy.BookId equals book.Id
                orderby loan.DueAtUtc
                select new OverdueLoanData(
                    loan.Id,
                    staff.EmployeeId,
                    staff.FirstName + " " + staff.LastName,
                    department.Name,
                    staff.DepartmentId,
                    staff.LineManagerEmail,
                    book.Title,
                    loan.DueAtUtc,
                    0))
            .ToListAsync(ct);
    }

    public async Task<DashboardCountsData> GetDashboardCountsAsync(DateTime overdueCutoffUtc, CancellationToken ct = default)
    {
        int totalBooks = await db.Books.AsNoTracking().CountAsync(book => !book.IsDeleted, ct);
        int totalCopies = await db.BookCopies.AsNoTracking().CountAsync(ct);
        int activeReaders = await db.StaffMembers.AsNoTracking().CountAsync(staff => staff.Status == StaffMemberStatus.Active, ct);
        int activeLoans = await db.Loans.AsNoTracking().CountAsync(
            loan => loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue, ct);
        int totalLoans = await db.Loans.AsNoTracking().CountAsync(ct);
        int overdueLoans = await db.Loans.AsNoTracking().CountAsync(
            loan => (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue)
                    && loan.DueAtUtc < overdueCutoffUtc, ct);
        int restrictedEmployees = await db.StaffMembers.AsNoTracking().CountAsync(
            staff => staff.Status == StaffMemberStatus.Restricted, ct);
        int suspendedEmployees = await db.StaffMembers.AsNoTracking().CountAsync(
            staff => staff.Status == StaffMemberStatus.Suspended, ct);

        return new DashboardCountsData(
            totalBooks,
            totalCopies,
            activeReaders,
            activeLoans,
            totalLoans,
            overdueLoans,
            restrictedEmployees,
            suspendedEmployees);
    }

    public async Task<IReadOnlyList<TopBookReportItemDto>> GetTopBooksAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default) => await (
            from loan in db.Loans.AsNoTracking()
            where loan.IssuedAtUtc >= fromUtc && loan.IssuedAtUtc <= toUtc
            join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id
            join book in db.Books.AsNoTracking() on copy.BookId equals book.Id
            group loan by new { book.Id, book.Title } into grouped
            orderby grouped.Count() descending, grouped.Key.Title
            select new TopBookReportItemDto(grouped.Key.Id, grouped.Key.Title, grouped.Count()))
            .Take(options.Value.TopBooksLimit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TopCategoryReportItemDto>> GetTopCategoriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default) => await (
            from loan in db.Loans.AsNoTracking()
            where loan.IssuedAtUtc >= fromUtc && loan.IssuedAtUtc <= toUtc
            join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id
            join book in db.Books.AsNoTracking() on copy.BookId equals book.Id
            join category in db.Categories.AsNoTracking() on book.CategoryId equals category.Id
            group loan by new { category.Id, category.Name } into grouped
            orderby grouped.Count() descending, grouped.Key.Name
            select new TopCategoryReportItemDto(grouped.Key.Id, grouped.Key.Name, grouped.Count()))
            .Take(options.Value.TopCategoriesLimit)
            .ToListAsync(ct);

   public async Task<IReadOnlyList<DepartmentEmployeesData>> GetDepartmentEmployeesAsync(
        CancellationToken ct = default) => await (
            from department in db.Departments.AsNoTracking()
            select new DepartmentEmployeesData(
                department.Id,
                department.Name,
                db.StaffMembers.Count(staff =>
                    staff.DepartmentId == department.Id && staff.Status != StaffMemberStatus.Deactivated)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DepartmentCategoryLoansData>> GetDepartmentCategoryLoansAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default) => await (
            from loan in db.Loans.AsNoTracking()
            where loan.IssuedAtUtc >= fromUtc && loan.IssuedAtUtc <= toUtc
            join staff in db.StaffMembers.AsNoTracking() on loan.StaffMemberId equals staff.Id
            join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id
            join book in db.Books.AsNoTracking() on copy.BookId equals book.Id
            join category in db.Categories.AsNoTracking() on book.CategoryId equals category.Id
            group loan by new { staff.DepartmentId, CategoryId = category.Id, category.Name } into grouped
            select new DepartmentCategoryLoansData(
                grouped.Key.DepartmentId, grouped.Key.CategoryId, grouped.Key.Name, grouped.Count()))
            .ToListAsync(ct);

    public Task<string?> GetCategoryNameAsync(Guid categoryId, CancellationToken ct = default) =>
        db.Categories.AsNoTracking()
            .Where(category => category.Id == categoryId)
            .Select(category => category.Name)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CopyCountData>> GetCopyCountsAsync(
        Guid? categoryId,
        CancellationToken ct = default)
    {
        IQueryable<Book> books = db.Books.AsNoTracking();
        if (categoryId is Guid id)
        {
            // The category and its subcategories, like the catalog search.
            books = books.Where(book =>
                book.CategoryId == id ||
                db.Categories.Any(category => category.Id == book.CategoryId && category.ParentCategoryId == id));
        }

        return await (
            from copy in db.BookCopies.AsNoTracking()
            join book in books on copy.BookId equals book.Id
            group copy by new { copy.Status, copy.Condition } into grouped
            select new CopyCountData(grouped.Key.Status, grouped.Key.Condition, grouped.Count()))
            .ToListAsync(ct);
    }
}
