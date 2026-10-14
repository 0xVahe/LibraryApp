using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Ports;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Reporting;

public sealed class ReportingReadRepository(LibraryDbContext db) : IReportingReadRepository
{
    public async Task<IReadOnlyList<OverdueLoanData>> GetOverdueLoansAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var activePoints = db.PenaltyRecords
            .AsNoTracking()
            .Where(p => p.Status != PenaltyRecordStatus.Waived)
            .GroupBy(p => p.LoanId)
            .Select(group => new { LoanId = group.Key, Points = group.Sum(p => p.Points) });

        return await (
            from loan in db.Loans.AsNoTracking()
            where (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue)
                  && loan.DueAtUtc < nowUtc
            join staff in db.StaffMembers.AsNoTracking() on loan.StaffMemberId equals staff.Id
            join department in db.Departments.AsNoTracking() on staff.DepartmentId equals department.Id
            join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id
            join book in db.Books.AsNoTracking() on copy.BookId equals book.Id
            join points in activePoints on loan.Id equals points.LoanId into pointsGroup
            from points in pointsGroup.DefaultIfEmpty()
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
                points == null ? 0 : points.Points))
            .ToListAsync(ct);
    }
}
