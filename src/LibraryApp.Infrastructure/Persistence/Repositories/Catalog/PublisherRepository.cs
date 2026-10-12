using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Catalog;

public sealed class PublisherRepository(LibraryDbContext db) : IPublisherRepository
{
    public async Task<IReadOnlyList<PublisherDto>> ListAsync(CancellationToken ct) =>
        await db.Publishers.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new PublisherDto(x.Id, x.Name)).ToListAsync(ct);

    public Task<Publisher?> FindAsync(Guid id, CancellationToken ct) =>
        db.Publishers.SingleOrDefaultAsync(x => x.Id == id, ct);

    public void Add(Publisher publisher) => db.Publishers.Add(publisher);
    public void Remove(Publisher publisher) => db.Publishers.Remove(publisher);

    /// <summary>Soft-deleted books count too: their rows stay in the table and the foreign key would block the delete.</summary>
    public Task<bool> IsInUseAsync(Guid id, CancellationToken ct) =>
        db.Books.IgnoreQueryFilters().AnyAsync(x => x.PublisherId == id, ct);
}
