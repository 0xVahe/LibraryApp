using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Dto;

/// <summary>CAT-10 creation input. Ownership and review metadata are always server supplied.</summary>
/// <param name="Title">Suggested book title, required and at most 300 characters.</param>
/// <param name="AuthorName">Suggested author name, required and at most 200 characters.</param>
/// <param name="Isbn">Optional valid ISBN-10 or ISBN-13.</param>
/// <param name="Reason">Reason for the suggestion, required and at most 1000 characters.</param>
public sealed record CreateBookSuggestionRequest(string? Title, string? AuthorName, string? Isbn, string? Reason);

/// <summary>Comment used when an administrator rejects a book suggestion.</summary>
/// <param name="Comment">Required rejection comment, at most 1000 characters.</param>
public sealed record RejectBookSuggestionRequest(string? Comment);

/// <summary>Filters and pagination for the administrator's book suggestion list.</summary>
/// <param name="Status">Optional status filter: Pending, Approved or Rejected.</param>
/// <param name="Page">Page number, starting at 1.</param>
/// <param name="PageSize">Items per page, default 20 and maximum 100.</param>
public sealed record BookSuggestionListRequest(
    BookSuggestionStatus? Status = null, int Page = 1, int PageSize = PageRequest.DefaultPageSize);

public sealed record BookSuggestionDto(
    Guid Id, Guid StaffMemberId, string StaffMemberName, string Title, string AuthorName, string? Isbn, string Reason,
    BookSuggestionStatus Status, DateTime CreatedAtUtc, DateTime? ReviewedAtUtc,
    Guid? ReviewedByUserId, string? ReviewComment);
