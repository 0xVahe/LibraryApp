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

    public async Task<PagedResult<BookSuggestionDto>> ListAsync(Guid? staffMemberId, BookSuggestionStatus? status,
        PageRequest paging, CancellationToken ct)
    {
        var query = db.Set<BookSuggestion>().AsNoTracking();
        if (staffMemberId is { } owner) query = query.Where(x => x.StaffMemberId == owner);
        if (status is { } state) query = query.Where(x => x.Status == state);

        var total = await query.CountAsync(ct);
        // Avoid PageRequest.Skip's integer overflow on a valid but very distant page.
        if ((long)(paging.Page - 1) * paging.PageSize >= total)
            return new PagedResult<BookSuggestionDto>([], total, paging.Page, paging.PageSize);

        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip(paging.Skip).Take(paging.PageSize)
            .Select(x => new BookSuggestionDto(x.Id, x.StaffMemberId, x.Title, x.AuthorName, x.Isbn, x.Reason,
                x.Status, x.CreatedAtUtc, x.ReviewedAtUtc, x.ReviewedByUserId, x.ReviewComment)).ToListAsync(ct);
        return new PagedResult<BookSuggestionDto>(items, total, paging.Page, paging.PageSize);
    }
}
