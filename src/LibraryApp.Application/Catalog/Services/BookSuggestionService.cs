using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>
/// Book purchase suggestions (CAT-10). The name of the staff member who made a suggestion comes from the Staff module
/// through <see cref="IStaffMemberService.GetNamesAsync"/>; Catalog never reads the Staff tables itself.
/// </summary>
public sealed class BookSuggestionService(IBookSuggestionRepository repository, IStaffMemberService staffMembers,
    IUnitOfWork unitOfWork, IApplicationLock applicationLock, ICurrentUser currentUser, IClock clock, IIdGenerator ids)
    : IBookSuggestionService
{
    /// <summary>Shown when the Staff module has no name for the staff member of a suggestion.</summary>
    public const string UnknownStaffMemberName = "Unknown";

    public Task<BookSuggestionDto> CreateAsync(CreateBookSuggestionRequest request, CancellationToken ct = default)
    {
        var staffMemberId = RequireStaffMemberId();
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            string? isbn = null;
            if (request.Isbn is not null)
            {
                if (!IsbnRules.TryNormalize(request.Isbn, out var normalized))
                    throw new DomainException(CommonErrors.ValidationFailed);
                isbn = normalized;
            }

            var suggestion = new BookSuggestion(ids.NewId(), staffMemberId, request.Title!, request.AuthorName!,
                isbn, request.Reason!, clock.UtcNow);
            repository.Add(suggestion);
            return await ToDtoAsync(suggestion, token);
        }, ct);
    }

    public async Task<PagedResult<BookSuggestionDto>> ListMineAsync(PageRequest paging, CancellationToken ct = default)
    {
        var staffMemberId = RequireStaffMemberId();
        var page = await repository.ListAsync(staffMemberId, null, paging, ct);
        return await ToDtoPageAsync(page, ct);
    }

    public async Task<PagedResult<BookSuggestionDto>> ListAsync(BookSuggestionListRequest request, CancellationToken ct = default)
    {
        var page = await repository.ListAsync(null, request.Status, new PageRequest(request.Page, request.PageSize), ct);
        return await ToDtoPageAsync(page, ct);
    }

    public Task<BookSuggestionDto> ApproveAsync(Guid id, CancellationToken ct = default) =>
        ReviewAsync(id, suggestion => suggestion.Approve(currentUser.UserId, clock.UtcNow), ct);

    public Task<BookSuggestionDto> RejectAsync(Guid id, RejectBookSuggestionRequest request, CancellationToken ct = default) =>
        ReviewAsync(id, suggestion => suggestion.Reject(currentUser.UserId, clock.UtcNow, request.Comment), ct);

    private Task<BookSuggestionDto> ReviewAsync(Guid id, Action<BookSuggestion> review, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // CAT-10: hold the SQL transaction lock through read, transition and commit across API instances.
            await applicationLock.AcquireAsync($"catalog:book-suggestion:{id:N}", token);
            var suggestion = await repository.FindAsync(id, token)
                ?? throw new DomainException(CatalogErrors.SuggestionNotFound);
            if (suggestion.Status != BookSuggestionStatus.Pending)
                throw new DomainException(CatalogErrors.SuggestionAlreadyReviewed);
            review(suggestion);
            return await ToDtoAsync(suggestion, token);
        }, ct);

    private Guid RequireStaffMemberId() => currentUser.StaffMemberId is { } id && id != Guid.Empty
        ? id
        : throw new DomainException(CatalogErrors.SuggestionStaffIdentityRequired);

    /// <summary>One suggestion (create, approve, reject): one name from the Staff module.</summary>
    private async Task<BookSuggestionDto> ToDtoAsync(BookSuggestion suggestion, CancellationToken ct)
    {
        var names = await staffMembers.GetNamesAsync([suggestion.StaffMemberId], ct);
        return ToDto(suggestion, names);
    }

    /// <summary>A page of suggestions: all names in one call to the Staff module, not one call per row (no N+1).</summary>
    private async Task<PagedResult<BookSuggestionDto>> ToDtoPageAsync(PagedResult<BookSuggestion> page, CancellationToken ct)
    {
        var staffMemberIds = page.Items.Select(x => x.StaffMemberId).Distinct().ToList();
        var names = await staffMembers.GetNamesAsync(staffMemberIds, ct);
        var items = page.Items.Select(x => ToDto(x, names)).ToList();
        return new PagedResult<BookSuggestionDto>(items, page.TotalCount, page.Page, page.PageSize);
    }

    private static BookSuggestionDto ToDto(BookSuggestion x, IReadOnlyDictionary<Guid, string> names)
    {
        var staffMemberName = names.TryGetValue(x.StaffMemberId, out var name) ? name : UnknownStaffMemberName;
        return new(x.Id, x.StaffMemberId, staffMemberName, x.Title, x.AuthorName,
            x.Isbn, x.Reason, x.Status, x.CreatedAtUtc, x.ReviewedAtUtc, x.ReviewedByUserId, x.ReviewComment);
    }
}
