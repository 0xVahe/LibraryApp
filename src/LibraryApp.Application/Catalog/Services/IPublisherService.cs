using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Services;

public interface IPublisherService
{
    Task<IReadOnlyList<PublisherDto>> ListAsync(CancellationToken ct = default);
    Task<PublisherDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<PublisherDto> CreateAsync(PublisherRequest request, CancellationToken ct = default);
    Task UpdateAsync(Guid id, PublisherRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
