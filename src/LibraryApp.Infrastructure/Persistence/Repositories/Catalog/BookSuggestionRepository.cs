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

    public async Task<PagedResult<BookSuggestion>> ListAsync(Guid? staffMemberId, BookSuggestionStatus? status,
        PageRequest paging, CancellationToken ct)
    {
        var query = db.Set<BookSuggestion>().AsNoTracking();
        if (staffMemberId is { } owner) query = query.Where(x => x.StaffMemberId == owner);
        if (status is { } state) query = query.Where(x => x.Status == state);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(ct);
        return new PagedResult<BookSuggestion>(items, total, paging.Page, paging.PageSize);
    }
}
