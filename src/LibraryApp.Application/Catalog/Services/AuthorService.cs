using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Authors and the authors of a book (CAT-02, BR-CAT-5); every change runs in one transaction.</summary>
public sealed class AuthorService(
    IAuthorRepository repository, IUnitOfWork unitOfWork, IIdGenerator ids, IApplicationLock applicationLock) : IAuthorService
{
    public Task<IReadOnlyList<AuthorDto>> ListAsync(CancellationToken ct = default) => repository.ListAsync(ct);

    public async Task<AuthorDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var author = await FindAsync(id, ct);
        return new AuthorDto(author.Id, author.FullName);
    }

    public Task<AuthorDto> CreateAsync(AuthorRequest request, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(token =>
        {
            var author = new Author(ids.NewId(), request.FullName!);
            repository.Add(author);
            return Task.FromResult(new AuthorDto(author.Id, author.FullName));
        }, ct);

    public Task UpdateAsync(Guid id, AuthorRequest request, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var author = await FindAsync(id, token);
            author.Rename(request.FullName!);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await applicationLock.AcquireAsync("catalog:authors", token);
            var author = await FindAsync(id, token);
            if (await repository.IsInUseAsync(id, token))
                throw new DomainException(CatalogErrors.AuthorInUse);
            repository.Remove(author);
        }, ct);

    public async Task<IReadOnlyList<Guid>> GetBookAuthorIdsAsync(Guid bookId, CancellationToken ct = default)
    {
        if (!await repository.BookExistsAsync(bookId, ct))
            throw new DomainException(CatalogErrors.BookNotFound);

        var authors = await repository.GetBookAuthorsAsync(bookId, ct);
        return authors.Select(x => x.AuthorId).ToArray();
    }

    public Task SetBookAuthorsAsync(Guid bookId, BookAuthorsRequest request, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (request.AuthorIds is not { Count: > 0 })
                throw new DomainException(CatalogErrors.BookAuthorsRequired);

            await applicationLock.AcquireAsync("catalog:authors", token);
            if (!await repository.BookExistsAsync(bookId, token))
                throw new DomainException(CatalogErrors.BookNotFound);

            var authorIds = request.AuthorIds.Distinct().ToArray();
            if (await repository.CountAuthorsAsync(authorIds, token) != authorIds.Length)
                throw new DomainException(CatalogErrors.AuthorNotFound);

            var existing = await repository.GetBookAuthorsAsync(bookId, token);
            foreach (var link in existing.Where(x => !authorIds.Contains(x.AuthorId)))
                repository.RemoveLink(link);
            foreach (var authorId in authorIds.Where(id => existing.All(x => x.AuthorId != id)))
                repository.AddLink(new BookAuthor(bookId, authorId));
        }, ct);

    private async Task<Author> FindAsync(Guid id, CancellationToken ct) =>
        await repository.FindAsync(id, ct) ?? throw new DomainException(CatalogErrors.AuthorNotFound);
}
