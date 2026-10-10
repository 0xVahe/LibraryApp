using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Services;

public sealed class BookService : IBookService
{
    private readonly IBookRepository _bookRepository;
    private readonly IAuthorRepository _authorRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIdGenerator _idGenerator;
    private readonly IApplicationLock _applicationLock;
    private readonly IClock _clock;

    public BookService(
        IBookRepository bookRepository,
        IAuthorRepository authorRepository,
        IUnitOfWork unitOfWork,
        IIdGenerator idGenerator,
        IApplicationLock applicationLock,
        IClock clock)
    {
        this._bookRepository = bookRepository;
        this._authorRepository = authorRepository;
        this._unitOfWork = unitOfWork;
        this._idGenerator = idGenerator;
        this._applicationLock = applicationLock;
        this._clock = clock;
    }

    public Task<Guid> CreateAsync(
        CreateBookRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // 1. Ensure that at least one author is provided.
            if (request.AuthorIds is not { Count: > 0 })
                throw new DomainException(CatalogErrors.BookAuthorsRequired);

            // 2. Serialize book creation and update operations.
            await _applicationLock.AcquireAsync("catalog:books", token);

            // 3. Verify that the category exists.
            if (!await _bookRepository.CategoryExistsAsync(
                    request.CategoryId, token))
            {
                throw new DomainException(CatalogErrors.CategoryNotFound);
            }

            // 4. Verify that the publisher exists when one is provided.
            if (request.PublisherId.HasValue &&
                !await _bookRepository.PublisherExistsAsync(
                    request.PublisherId.Value, token))
            {
                throw new DomainException(CatalogErrors.PublisherNotFound);
            }

            // 5. Ensure that the ISBN is unique among active books.
            if (!IsbnRules.TryNormalize(request.Isbn, out var isbn))
                throw new DomainException(CommonErrors.ValidationFailed);

            if (await _bookRepository.IsbnExistsAsync(isbn, null, token))
                throw new DomainException(CatalogErrors.BookIsbnDuplicate);

            // 6. Verify that all provided authors exist.
            var authorIds = request.AuthorIds.Distinct().ToArray();

            if (await _authorRepository.CountAuthorsAsync(
                    authorIds, token) != authorIds.Length)
            {
                throw new DomainException(CatalogErrors.AuthorNotFound);
            }

            // 7. Create the book entity.
            var book = new Book(
                _idGenerator.NewId(),
                request.Title,
                isbn,
                request.PublicationYear,
                request.Language,
                request.Description,
                request.CategoryId,
                request.PublisherId);

            _bookRepository.Add(book);

            // 8. Create a link between the book and each author.
            foreach (var authorId in authorIds)
            {
                _authorRepository.AddLink(
                    new BookAuthor(book.Id, authorId));
            }

            // 9. Return the ID of the created book.
            return book.Id;
        }, ct);
    }

    public Task UpdateAsync(
        Guid id,
        UpdateBookRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // 1. Serialize book creation and update operations.
            await _applicationLock.AcquireAsync("catalog:books", token);

            // 2. Find the book and ensure that it is active.
            var book = await _bookRepository.FindAsync(id, token);

            if (book is null || book.IsDeleted)
                throw new DomainException(CatalogErrors.BookNotFound);

            // 3. Verify that the category exists.
            if (!await _bookRepository.CategoryExistsAsync(
                    request.CategoryId, token))
            {
                throw new DomainException(CatalogErrors.CategoryNotFound);
            }

            // 4. Verify that the publisher exists when one is provided.
            if (request.PublisherId.HasValue &&
                !await _bookRepository.PublisherExistsAsync(
                    request.PublisherId.Value, token))
            {
                throw new DomainException(CatalogErrors.PublisherNotFound);
            }

            // 5. Ensure that the ISBN is unique among other active books.
            if (!IsbnRules.TryNormalize(request.Isbn, out var isbn))
                throw new DomainException(CommonErrors.ValidationFailed);

            if (await _bookRepository.IsbnExistsAsync(isbn, id, token))
                throw new DomainException(CatalogErrors.BookIsbnDuplicate);

            // 6. Update the book details.
            book.Update(
                request.Title,
                isbn,
                request.PublicationYear,
                request.Language,
                request.Description,
                request.CategoryId,
                request.PublisherId);
        }, ct);
    }

    public Task DeleteAsync(
        Guid id,
        CancellationToken ct = default) =>
        _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // 1. Serialize book deletion with other book operations.
            await _applicationLock.AcquireAsync("catalog:books", token);

            // 2. Find the book and ensure that it is active.
            var book = await _bookRepository.FindAsync(id, token);

            if (book is null || book.IsDeleted)
                throw new DomainException(CatalogErrors.BookNotFound);

            // 3. Ensure that no copies are on loan or reserved.
            if (await _bookRepository.HasCopiesInCirculationAsync(id, token))
                throw new DomainException(CatalogErrors.BookHasCopiesInCirculation);

            // 4. Mark the book as deleted without removing its record.
            book.Delete(_clock.UtcNow);
        }, ct);
}