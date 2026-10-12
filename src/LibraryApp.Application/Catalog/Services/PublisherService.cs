using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Catalog.Services;

public sealed class PublisherService(
    IPublisherRepository repository, IUnitOfWork unitOfWork, IIdGenerator ids, IApplicationLock applicationLock) : IPublisherService
{
    public Task<IReadOnlyList<PublisherDto>> ListAsync(CancellationToken ct = default) => repository.ListAsync(ct);

    public async Task<PublisherDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var publisher = await FindAsync(id, ct);
        return new PublisherDto(publisher.Id, publisher.Name);
    }

    public Task<PublisherDto> CreateAsync(PublisherRequest request, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(token =>
        {
            var publisher = new Publisher(ids.NewId(), request.Name!);
            repository.Add(publisher);
            return Task.FromResult(new PublisherDto(publisher.Id, publisher.Name));
        }, ct);

    public Task UpdateAsync(Guid id, PublisherRequest request, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var publisher = await FindAsync(id, token);
            publisher.Rename(request.Name!);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await applicationLock.AcquireAsync("catalog:publishers", token);
            var publisher = await FindAsync(id, token);
            if (await repository.IsInUseAsync(id, token))
                throw new DomainException(CatalogErrors.PublisherInUse);
            repository.Remove(publisher);
        }, ct);

    private async Task<Publisher> FindAsync(Guid id, CancellationToken ct) =>
        await repository.FindAsync(id, ct) ?? throw new DomainException(CatalogErrors.PublisherNotFound);
}
