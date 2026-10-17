using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Catalog.Services;

public sealed class BookSuggestionService(IBookSuggestionRepository repository, IUnitOfWork unitOfWork,
    IApplicationLock applicationLock, ICurrentUser currentUser, IClock clock, IIdGenerator ids) : IBookSuggestionService
{
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

    public Task<PagedResult<BookSuggestionDto>> ListMineAsync(PageRequest paging, CancellationToken ct = default) =>
        repository.ListAsync(RequireStaffMemberId(), null, paging, ct);

    public Task<PagedResult<BookSuggestionDto>> ListAsync(BookSuggestionListRequest request, CancellationToken ct = default) =>
        repository.ListAsync(null, request.Status, new PageRequest(request.Page, request.PageSize), ct);

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

    private async Task<BookSuggestionDto> ToDtoAsync(BookSuggestion x, CancellationToken ct)
    {
        var staffMemberName = await repository.GetStaffMemberNameAsync(x.StaffMemberId, ct);
        return new(x.Id, x.StaffMemberId, staffMemberName, x.Title, x.AuthorName,
            x.Isbn, x.Reason, x.Status, x.CreatedAtUtc, x.ReviewedAtUtc, x.ReviewedByUserId, x.ReviewComment);
    }
}
