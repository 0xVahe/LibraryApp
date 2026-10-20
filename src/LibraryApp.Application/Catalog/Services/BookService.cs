using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>
/// Books (CAT-01): create (one, or a batch for the CSV import), update and soft delete. Every change runs in one
/// transaction under the "catalog:books" lock, so two requests cannot both take the same ISBN.
/// </summary>
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
            // Serialize book creation and update operations.
            await _applicationLock.AcquireAsync("catalog:books", token);

            return await AddBookAsync(request, token);
        }, ct);
    }

    public Task<IReadOnlyList<Guid>> CreateBatchAsync(
        IReadOnlyList<CreateBookRequest> requests,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        return _unitOfWork.ExecuteInTransactionAsync<IReadOnlyList<Guid>>(async token =>
        {
            // One lock and one transaction for the whole batch: all books are saved together, or none.
            await _applicationLock.AcquireAsync("catalog:books", token);

            // The database check below does not see the books added earlier in this batch, so the batch checks its own ISBNs.
            var isbnsInBatch = new HashSet<string>(StringComparer.Ordinal);
            var ids = new List<Guid>(requests.Count);

            foreach (var request in requests)
            {
                ArgumentNullException.ThrowIfNull(request);

                if (IsbnRules.TryNormalize(request.Isbn, out var isbn) && !isbnsInBatch.Add(isbn))
                    throw new DomainException(CatalogErrors.BookIsbnDuplicate);

                ids.Add(await AddBookAsync(request, token));
            }

            return ids;
        }, ct);
    }

    /// <summary>
    /// The rules and the writes of one new book (CAT-01, BR-CAT-1, BR-CAT-5). Runs inside the caller's transaction,
    /// after the caller took the "catalog:books" lock.
    /// </summary>
    private async Task<Guid> AddBookAsync(CreateBookRequest request, CancellationToken token)
    {
        if (request.AuthorIds is not { Count: > 0 })
            throw new DomainException(CatalogErrors.BookAuthorsRequired);

        if (!await _bookRepository.CategoryExistsAsync(
                request.CategoryId, token))
        {
            throw new DomainException(CatalogErrors.CategoryNotFound);
        }

        if (request.PublisherId.HasValue &&
            !await _bookRepository.PublisherExistsAsync(
                request.PublisherId.Value, token))
        {
            throw new DomainException(CatalogErrors.PublisherNotFound);
        }

        // BR-CAT-1: the ISBN is stored as 13 digits and is unique among active books.
        if (!IsbnRules.TryNormalize(request.Isbn, out var isbn))
            throw new DomainException(CommonErrors.ValidationFailed);

        if (await _bookRepository.IsbnExistsAsync(isbn, null, token))
            throw new DomainException(CatalogErrors.BookIsbnDuplicate);

        var authorIds = request.AuthorIds.Distinct().ToArray();

        if (await _authorRepository.CountAuthorsAsync(
                authorIds, token) != authorIds.Length)
        {
            throw new DomainException(CatalogErrors.AuthorNotFound);
        }

        var book = new Book(
            _idGenerator.NewId(),
            request.Title,
            isbn,
            request.PublicationYear,
            request.Language,
            request.Description,
            request.CategoryId,
            request.PublisherId,
            request.CoverImagePath);

        _bookRepository.Add(book);

        foreach (var authorId in authorIds)
        {
            _authorRepository.AddLink(
                new BookAuthor(book.Id, authorId));
        }

        return book.Id;
    }

    public Task UpdateAsync(
        Guid id,
        UpdateBookRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Serialize book creation and update operations.
            await _applicationLock.AcquireAsync("catalog:books", token);

            var book = await _bookRepository.FindAsync(id, token);

            if (book is null || book.IsDeleted)
                throw new DomainException(CatalogErrors.BookNotFound);

            if (!await _bookRepository.CategoryExistsAsync(
                    request.CategoryId, token))
            {
                throw new DomainException(CatalogErrors.CategoryNotFound);
            }

            if (request.PublisherId.HasValue &&
                !await _bookRepository.PublisherExistsAsync(
                    request.PublisherId.Value, token))
            {
                throw new DomainException(CatalogErrors.PublisherNotFound);
            }

            // BR-CAT-1: unique among the other active books.
            if (!IsbnRules.TryNormalize(request.Isbn, out var isbn))
                throw new DomainException(CommonErrors.ValidationFailed);

            if (await _bookRepository.IsbnExistsAsync(isbn, id, token))
                throw new DomainException(CatalogErrors.BookIsbnDuplicate);

            book.Update(
                request.Title,
                isbn,
                request.PublicationYear,
                request.Language,
                request.Description,
                request.CategoryId,
                request.PublisherId,
                request.CoverImagePath);
        }, ct);
    }

    public Task DeleteAsync(
        Guid id,
        CancellationToken ct = default) =>
        _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Serialize book deletion with other book operations.
            await _applicationLock.AcquireAsync("catalog:books", token);

            var book = await _bookRepository.FindAsync(id, token);

            if (book is null || book.IsDeleted)
                throw new DomainException(CatalogErrors.BookNotFound);

            // BR-CAT-3: no deletion while a copy is on loan or reserved.
            var copies = await _bookRepository.GetCopiesForUpdateAsync(id, token);

            if (copies.Any(c =>
                    c.Status is BookCopyStatus.OnLoan or BookCopyStatus.Reserved))
            {
                throw new DomainException(CatalogErrors.BookHasCopiesInCirculation);
            }

            // The other copies are withdrawn; Lost copies stay Lost because Lost is a final status.
            foreach (var copy in copies.Where(
                         c => c.CanChangeStatusTo(BookCopyStatus.Withdrawn)))
            {
                copy.ChangeStatus(BookCopyStatus.Withdrawn);
            }

            book.Delete(_clock.UtcNow);
        }, ct);
}
