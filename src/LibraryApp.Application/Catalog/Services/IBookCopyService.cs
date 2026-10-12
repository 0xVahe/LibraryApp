using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Physical copies of a book: list, add, edit, take out of circulation (CAT-04).</summary>
public interface IBookCopyService
{
    Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(Guid bookId, CancellationToken ct = default);

    Task<BookCopyDto> GetCopyAsync(Guid bookId, Guid copyId, CancellationToken ct = default);

    Task<BookCopyDto> AddCopyAsync(Guid bookId, AddCopyRequest request, CancellationToken ct = default);

    Task<BookCopyDto> UpdateCopyAsync(Guid bookId, Guid copyId, UpdateCopyRequest request, CancellationToken ct = default);

    Task<BookCopyDto> ChangeStatusAsync(Guid bookId, Guid copyId, ChangeCopyStatusRequest request, CancellationToken ct = default);
}
