using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Dto;

/// <summary>CAT-10 creation input. Ownership and review metadata are always server supplied.</summary>
public sealed record CreateBookSuggestionRequest(string? Title, string? AuthorName, string? Isbn, string? Reason);

public sealed record RejectBookSuggestionRequest(string? Comment);

public sealed record BookSuggestionListRequest(
    BookSuggestionStatus? Status = null, int Page = 1, int PageSize = PageRequest.DefaultPageSize);

public sealed record BookSuggestionDto(
    Guid Id, Guid StaffMemberId, string Title, string AuthorName, string? Isbn, string Reason,
    BookSuggestionStatus Status, DateTime CreatedAtUtc, DateTime? ReviewedAtUtc,
    Guid? ReviewedByUserId, string? ReviewComment);
