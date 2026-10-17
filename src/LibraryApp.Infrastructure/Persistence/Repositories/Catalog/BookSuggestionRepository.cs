using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Catalog;

public sealed class BookSuggestionRepository(LibraryDbContext db) : IBookSuggestionRepository
{
    public void Add(BookSuggestion suggestion) => db.Set<BookSuggestion>().Add(suggestion);

    public Task<BookSuggestion?> FindAsync(Guid id, CancellationToken ct) =>
        db.Set<BookSuggestion>().SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<string> GetStaffMemberNameAsync(Guid staffMemberId, CancellationToken ct) =>
        db.StaffMembers.Where(x => x.Id == staffMemberId)
            .Select(x => x.FirstName + " " + x.LastName).SingleAsync(ct);

    public async Task<PagedResult<BookSuggestionDto>> ListAsync(Guid? staffMemberId, BookSuggestionStatus? status,
        PageRequest paging, CancellationToken ct)
    {
        var query = db.Set<BookSuggestion>().AsNoTracking();
        if (staffMemberId is { } owner) query = query.Where(x => x.StaffMemberId == owner);
        if (status is { } state) query = query.Where(x => x.Status == state);

        var total = await query.CountAsync(ct);
        var items = await (from x in query
            join staff in db.StaffMembers on x.StaffMemberId equals staff.Id
            orderby x.CreatedAtUtc descending, x.Id
            select new BookSuggestionDto(x.Id, x.StaffMemberId, staff.FirstName + " " + staff.LastName,
                x.Title, x.AuthorName, x.Isbn, x.Reason, x.Status, x.CreatedAtUtc, x.ReviewedAtUtc,
                x.ReviewedByUserId, x.ReviewComment)).Skip(paging.Skip).Take(paging.PageSize).ToListAsync(ct);
        return new PagedResult<BookSuggestionDto>(items, total, paging.Page, paging.PageSize);
    }
}
