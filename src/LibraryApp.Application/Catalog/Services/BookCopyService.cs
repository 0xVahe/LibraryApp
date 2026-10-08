using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>
/// Copy management for librarians (CAT-04). Every change runs in <see cref="IUnitOfWork"/>, so a concurrent change of
/// the same copy (RowVersion) or a duplicate barcode caught by the database index becomes a 409.
/// </summary>
public sealed class BookCopyService(
    IBookCopyRepository copies,
    IUnitOfWork unitOfWork,
    IIdGenerator ids) : IBookCopyService
{
    public async Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(Guid bookId, CancellationToken ct = default)
    {
        BookState book = await copies.GetBookStateAsync(bookId, ct);
        if (book == BookState.NotFound)
            throw new DomainException(CatalogErrors.BookNotFound);

        // A deleted book keeps its copies: the librarian can still see them.
        IReadOnlyList<BookCopy> bookCopies = await copies.GetByBookAsync(bookId, ct);
        return bookCopies.Select(ToDto).ToList();
    }

    public async Task<BookCopyDto> GetCopyAsync(Guid bookId, Guid copyId, CancellationToken ct = default)
    {
        BookCopy copy = await GetCopyOfBookAsync(bookId, copyId, ct);
        return ToDto(copy);
    }

    public Task<BookCopyDto> AddCopyAsync(Guid bookId, AddCopyRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            BookState book = await copies.GetBookStateAsync(bookId, token);
            if (book == BookState.NotFound)
                throw new DomainException(CatalogErrors.BookNotFound);
            if (book == BookState.Deleted)
                throw new DomainException(CatalogErrors.BookDeleted);

            // The entity trims and upper-cases the barcode, so the duplicate check uses exactly the stored value.
            var copy = new BookCopy(ids.NewId(), bookId, request.Barcode, request.Condition, request.ShelfLocation);

            if (await copies.BarcodeExistsAsync(copy.Barcode, token))
                throw new DomainException(CatalogErrors.CopyBarcodeDuplicate);

            copies.Add(copy);
            return ToDto(copy);
        }, ct);
    }

    public Task<BookCopyDto> UpdateCopyAsync(Guid bookId, Guid copyId, UpdateCopyRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            BookCopy copy = await GetCopyOfBookAsync(bookId, copyId, token);
            copy.UpdateDetails(request.Condition, request.ShelfLocation);
            return ToDto(copy);
        }, ct);
    }

    public Task<BookCopyDto> ChangeStatusAsync(Guid bookId, Guid copyId, ChangeCopyStatusRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            BookCopy copy = await GetCopyOfBookAsync(bookId, copyId, token);

            EnsureLibrarianMayChange(copy, request.Status);
            copy.ChangeStatus(request.Status);           // the transition table: Lost and Withdrawn are final

            return ToDto(copy);
        }, ct);
    }

    /// <summary>
    /// BR-CAT-4. A copy that is OnLoan or Reserved belongs to a loan or a reservation, so only Borrowing changes it.
    /// Otherwise the librarian may set UnderRepair, Lost or Withdrawn, and Available only to end a repair
    /// (temporary decision until the BA answers who returns a repaired copy).
    /// </summary>
    private static void EnsureLibrarianMayChange(BookCopy copy, BookCopyStatus newStatus)
    {
        if (copy.Status is BookCopyStatus.OnLoan or BookCopyStatus.Reserved)
            throw new DomainException(CatalogErrors.CopyInCirculation);

        bool allowed = newStatus switch
        {
            BookCopyStatus.UnderRepair => true,
            BookCopyStatus.Lost => true,
            BookCopyStatus.Withdrawn => true,
            BookCopyStatus.Available => copy.Status == BookCopyStatus.UnderRepair,
            _ => false,                                  // OnLoan, Reserved: only Borrowing
        };

        if (!allowed)
            throw new DomainException(CatalogErrors.CopyStatusNotAllowedForLibrarian(newStatus));
    }

    /// <summary>The copy, if it exists and belongs to this book; otherwise 404 (the same answer for both cases).</summary>
    private async Task<BookCopy> GetCopyOfBookAsync(Guid bookId, Guid copyId, CancellationToken ct)
    {
        BookCopy? copy = await copies.GetByIdAsync(copyId, ct);
        if (copy is null || copy.BookId != bookId)
            throw new DomainException(CatalogErrors.CopyNotFound);

        return copy;
    }

    private static BookCopyDto ToDto(BookCopy copy) =>
        new(copy.Id, copy.BookId, copy.Barcode, copy.Status, copy.Condition, copy.ShelfLocation);
}
