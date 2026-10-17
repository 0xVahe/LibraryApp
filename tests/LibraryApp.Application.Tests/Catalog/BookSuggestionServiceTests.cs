using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Catalog;

public class BookSuggestionServiceTests
{
    private readonly TestTransaction _transaction = new();
    private readonly FakeCurrentUser _user = new();
    private readonly TestClock _clock = new(new DateTime(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc));

    private BookSuggestionService Service(Repository repository) =>
        new(repository, _transaction, _transaction, _user, _clock, _transaction);

    [Fact]
    public async Task Creation_uses_current_staff_and_clock_inside_transaction()
    {
        var repository = new Repository(_transaction);
        var dto = await Service(repository).CreateAsync(new("Title", "Author", null, "Reason"));
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal(_user.StaffMemberId, dto.StaffMemberId);
        Assert.Equal("Test Member", dto.StaffMemberName);
        Assert.Equal(_clock.UtcNow, dto.CreatedAtUtc);
        Assert.Equal(BookSuggestionStatus.Pending, dto.Status);
        Assert.Equal(dto.Id, repository.Suggestion!.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_empty_staff_identity_forbids_creation_and_own_list(bool empty)
    {
        _user.StaffMemberId = empty ? Guid.Empty : null;
        var repository = new Repository(_transaction);
        var service = Service(repository);
        var create = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(new("Title", "Author", null, "Reason")));
        var list = await Assert.ThrowsAsync<DomainException>(() => service.ListMineAsync(new PageRequest()));
        Assert.Equal(CatalogErrors.SuggestionStaffIdentityRequired, create.Error);
        Assert.Equal(CatalogErrors.SuggestionStaffIdentityRequired, list.Error);
        Assert.Null(repository.Suggestion);
        Assert.Equal(0, repository.ListCalls);
    }

    [Fact]
    public async Task Own_lists_always_pass_the_current_identity_to_the_database_port()
    {
        var repository = new Repository(_transaction);
        var service = Service(repository);
        var first = _user.StaffMemberId;
        await service.ListMineAsync(new PageRequest(2, 5));
        Assert.Equal(first, repository.Owner);
        Assert.Equal(new PageRequest(2, 5), repository.Paging);
        _user.StaffMemberId = Guid.NewGuid();
        await service.ListMineAsync(new PageRequest());
        Assert.Equal(_user.StaffMemberId, repository.Owner);
        Assert.NotEqual(first, repository.Owner);
    }

    [Fact]
    public async Task Administrator_list_passes_status_and_normalized_paging_without_owner_filter()
    {
        var repository = new Repository(_transaction);
        await Service(repository).ListAsync(new(BookSuggestionStatus.Rejected, -1, 101));
        Assert.Null(repository.Owner);
        Assert.Equal(BookSuggestionStatus.Rejected, repository.Status);
        Assert.Equal(new PageRequest(1, 100), repository.Paging);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Review_locks_before_reading_inside_transaction_and_uses_current_reviewer(bool reject)
    {
        var repository = new Repository(_transaction);
        var service = Service(repository);
        var created = await service.CreateAsync(new("Title", "Author", null, "Reason"));
        _user.StaffMemberId = Guid.NewGuid();
        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        var result = reject ? await service.RejectAsync(created.Id, new("Comment")) : await service.ApproveAsync(created.Id);
        Assert.Equal(created.StaffMemberId, result.StaffMemberId);
        Assert.Equal("Test Member", result.StaffMemberName);
        Assert.Equal(_user.UserId, result.ReviewedByUserId);
        Assert.Equal(_clock.UtcNow, result.ReviewedAtUtc);
        Assert.Equal(reject ? BookSuggestionStatus.Rejected : BookSuggestionStatus.Approved, result.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_review_id_is_not_found(bool reject)
    {
        var service = Service(new Repository(_transaction));
        var error = await Assert.ThrowsAsync<DomainException>(() => reject
            ? service.RejectAsync(Guid.NewGuid(), new("Comment")) : service.ApproveAsync(Guid.NewGuid()));
        Assert.Equal(CatalogErrors.SuggestionNotFound, error.Error);
    }

    private sealed class Repository(TestTransaction transaction) : IBookSuggestionRepository
    {
        public BookSuggestion? Suggestion { get; private set; }
        public Guid? Owner { get; private set; }
        public BookSuggestionStatus? Status { get; private set; }
        public PageRequest? Paging { get; private set; }
        public int ListCalls { get; private set; }

        public void Add(BookSuggestion suggestion)
        {
            Assert.True(transaction.Active);
            Suggestion = suggestion;
        }

        public Task<BookSuggestion?> FindAsync(Guid id, CancellationToken ct)
        {
            Assert.True(transaction.Active);
            Assert.Equal($"catalog:book-suggestion:{id:N}", transaction.Locks.Last());
            return Task.FromResult(Suggestion?.Id == id ? Suggestion : null);
        }

        public Task<string> GetStaffMemberNameAsync(Guid staffMemberId, CancellationToken ct)
        {
            Assert.True(transaction.Active);
            Assert.Equal(Suggestion!.StaffMemberId, staffMemberId);
            return Task.FromResult("Test Member");
        }

        public Task<PagedResult<BookSuggestionDto>> ListAsync(Guid? staffMemberId, BookSuggestionStatus? status,
            PageRequest paging, CancellationToken ct)
        {
            ListCalls++;
            (Owner, Status, Paging) = (staffMemberId, status, paging);
            return Task.FromResult(PagedResult<BookSuggestionDto>.Empty(paging));
        }
    }
}
