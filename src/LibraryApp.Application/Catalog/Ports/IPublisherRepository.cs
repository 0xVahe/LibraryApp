using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Ports;

public interface IPublisherRepository
{
    Task<IReadOnlyList<PublisherDto>> ListAsync(CancellationToken ct);
    Task<Publisher?> FindAsync(Guid id, CancellationToken ct);
    void Add(Publisher publisher);
    void Remove(Publisher publisher);
    Task<bool> IsInUseAsync(Guid id, CancellationToken ct);
}
