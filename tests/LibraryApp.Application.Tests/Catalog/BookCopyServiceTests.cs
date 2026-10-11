using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Catalog;

public class BookCopyServiceTests
{
    private readonly FakeBookCopyRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeReservationQueue _queue = new();
    private readonly BookCopyService _service;

    public BookCopyServiceTests()
    {
        _service = new BookCopyService(_repository, _unitOfWork, new FakeIdGenerator(), _queue);
    }

    // ---------- add (CAT-04, BR-CAT-2) ----------

    [Fact]
    public async Task A_new_copy_is_available_with_a_trimmed_upper_case_barcode()
    {
        Guid bookId = _repository.AddBook();

        var copy = await _service.AddCopyAsync(bookId, new AddCopyRequest(" bk-0001-01 ", BookCopyCondition.New, " A-03-2 "));

        Assert.Equal(BookCopyStatus.Available, copy.Status);
        Assert.Equal("BK-0001-01", copy.Barcode);
        Assert.Equal("A-03-2", copy.ShelfLocation);
        Assert.Equal(bookId, copy.BookId);
        Assert.Single(_repository.Copies);
        Assert.Equal(1, _unitOfWork.Transactions);
    }

    [Fact]
    public async Task A_copy_of_an_unknown_book_is_refused()
    {
        var error = await ErrorOf(() => _service.AddCopyAsync(Guid.NewGuid(), NewCopy("BK-1")));

        Assert.Equal("catalog.book_not_found", error.Code);
        Assert.Equal(ErrorKind.NotFound, error.Kind);
        Assert.Empty(_repository.Copies);
    }

    [Fact]
    public async Task A_copy_of_a_deleted_book_is_refused()
    {
        Guid bookId = _repository.AddBook(BookState.Deleted);

        var error = await ErrorOf(() => _service.AddCopyAsync(bookId, NewCopy("BK-1")));

        Assert.Equal("catalog.book_deleted", error.Code);
        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Empty(_repository.Copies);
    }

    [Theory]
    [InlineData(BookCopyStatus.Available)]
    [InlineData(BookCopyStatus.Withdrawn)]   // BR-CAT-2: retired copies keep their barcode
    [InlineData(BookCopyStatus.Lost)]
    public async Task A_barcode_used_by_any_copy_is_a_conflict(BookCopyStatus existingStatus)
    {
        Guid otherBookId = _repository.AddBook();
        _repository.AddCopy(otherBookId, "BK-0001-01", existingStatus);
        Guid bookId = _repository.AddBook();

        var error = await ErrorOf(() => _service.AddCopyAsync(bookId, NewCopy(" bk-0001-01")));

        Assert.Equal("catalog.copy_barcode_duplicate", error.Code);
        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Single(_repository.Copies);
    }

    // ---------- list and read ----------

    [Fact]
    public async Task The_list_has_only_the_copies_of_the_book_ordered_by_barcode()
    {
        Guid bookId = _repository.AddBook();
        _repository.AddCopy(bookId, "BK-2");
        _repository.AddCopy(bookId, "BK-1", BookCopyStatus.Withdrawn);
        _repository.AddCopy(_repository.AddBook(), "BK-3");

        var copies = await _service.GetCopiesAsync(bookId);

        Assert.Equal(new[] { "BK-1", "BK-2" }, copies.Select(c => c.Barcode));
    }

    [Fact]
    public async Task The_list_of_an_unknown_book_is_not_found()
    {
        var error = await ErrorOf(() => _service.GetCopiesAsync(Guid.NewGuid()));

        Assert.Equal("catalog.book_not_found", error.Code);
    }

    [Fact]
    public async Task A_copy_asked_through_another_book_is_not_found()
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(_repository.AddBook(), "BK-1");

        var read = await ErrorOf(() => _service.GetCopyAsync(bookId, copy.Id));
        var update = await ErrorOf(() => _service.UpdateCopyAsync(bookId, copy.Id, new UpdateCopyRequest(BookCopyCondition.Poor, null)));
        var status = await ErrorOf(() => _service.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(BookCopyStatus.Lost)));

        Assert.All(new[] { read, update, status }, e => Assert.Equal("catalog.copy_not_found", e.Code));
        Assert.Equal(BookCopyStatus.Available, copy.Status);
    }

    // ---------- edit ----------

    [Fact]
    public async Task Editing_changes_condition_and_shelf_but_not_the_status()
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(bookId, "BK-1");

        var result = await _service.UpdateCopyAsync(bookId, copy.Id, new UpdateCopyRequest(BookCopyCondition.Poor, "B-07"));

        Assert.Equal(BookCopyCondition.Poor, result.Condition);
        Assert.Equal("B-07", result.ShelfLocation);
        Assert.Equal(BookCopyStatus.Available, result.Status);
        Assert.Equal(1, _unitOfWork.Transactions);
    }

    // ---------- status (BR-CAT-4) ----------

    [Theory]
    [InlineData(BookCopyStatus.UnderRepair)]
    [InlineData(BookCopyStatus.Lost)]
    [InlineData(BookCopyStatus.Withdrawn)]
    public async Task A_librarian_takes_an_available_copy_out_of_circulation(BookCopyStatus newStatus)
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(bookId, "BK-1");

        var result = await _service.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(newStatus));

        Assert.Equal(newStatus, result.Status);
        Assert.Equal(newStatus, copy.Status);
    }

    [Theory]
    [InlineData(BookCopyStatus.OnLoan)]
    [InlineData(BookCopyStatus.Reserved)]
    public async Task Only_Borrowing_sets_OnLoan_and_Reserved(BookCopyStatus newStatus)
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(bookId, "BK-1");

        var error = await ErrorOf(() => _service.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(newStatus)));

        Assert.Equal("catalog.copy_status_not_allowed", error.Code);
        Assert.Equal(ErrorKind.BusinessRule, error.Kind);
        Assert.Equal(BookCopyStatus.Available, copy.Status);
    }

    [Theory]
    [InlineData(BookCopyStatus.OnLoan)]
    [InlineData(BookCopyStatus.Reserved)]
    public async Task A_copy_on_loan_or_reserved_is_changed_only_through_Borrowing(BookCopyStatus currentStatus)
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(bookId, "BK-1", currentStatus);

        var error = await ErrorOf(() => _service.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(BookCopyStatus.Lost)));

        Assert.Equal("catalog.copy_in_circulation", error.Code);
        Assert.Equal(currentStatus, copy.Status);
    }

    [Fact]
    public async Task A_repaired_copy_becomes_available_again()
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(bookId, "BK-1", BookCopyStatus.UnderRepair);

        var result = await _service.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(BookCopyStatus.Available));

        Assert.Equal(BookCopyStatus.Available, result.Status);
    }

    [Fact]
    public async Task A_lost_copy_does_not_come_back()
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(bookId, "BK-1", BookCopyStatus.Lost);

        var error = await ErrorOf(() => _service.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(BookCopyStatus.Available)));

        Assert.Equal("catalog.copy_status_not_allowed", error.Code);
        Assert.Equal(BookCopyStatus.Lost, copy.Status);
    }

    [Fact]
    public async Task A_withdrawn_copy_cannot_change_status_any_more()
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(bookId, "BK-1", BookCopyStatus.Withdrawn);

        var error = await ErrorOf(() => _service.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(BookCopyStatus.Lost)));

        Assert.Equal("catalog.copy_status_change_not_allowed", error.Code);
        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Equal(BookCopyStatus.Withdrawn, copy.Status);
    }

    // ---------- offer to the reservation queue (BQ-G1-10, BOR-07) ----------

    [Fact]
    public async Task A_new_copy_is_offered_to_the_reservation_queue_inside_the_transaction()
    {
        Guid bookId = _repository.AddBook();
        bool offeredInsideTransaction = false;
        _queue.OnOffer = _ =>
        {
            offeredInsideTransaction = _unitOfWork.Active;
            return Task.CompletedTask;
        };

        var copy = await _service.AddCopyAsync(bookId, NewCopy("BK-1"));

        Assert.Equal(new[] { (copy.Id, bookId) }, _queue.Offers);
        Assert.True(offeredInsideTransaction);
    }

    [Fact]
    public async Task A_refused_copy_is_not_offered()
    {
        Guid deletedBookId = _repository.AddBook(BookState.Deleted);
        Guid bookId = _repository.AddBook();
        _repository.AddCopy(bookId, "BK-1");

        await ErrorOf(() => _service.AddCopyAsync(deletedBookId, NewCopy("BK-2")));
        await ErrorOf(() => _service.AddCopyAsync(bookId, NewCopy("BK-1")));

        Assert.Empty(_queue.Offers);
    }

    [Fact]
    public async Task A_repaired_copy_is_offered_to_the_reservation_queue()
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(bookId, "BK-1", BookCopyStatus.UnderRepair);

        await _service.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(BookCopyStatus.Available));

        Assert.Equal(new[] { (copy.Id, bookId) }, _queue.Offers);
    }

    [Theory]
    [InlineData(BookCopyStatus.UnderRepair)]
    [InlineData(BookCopyStatus.Lost)]
    [InlineData(BookCopyStatus.Withdrawn)]
    public async Task Taking_a_copy_out_of_circulation_offers_nothing(BookCopyStatus newStatus)
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(bookId, "BK-1");

        await _service.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(newStatus));

        Assert.Empty(_queue.Offers);
    }

    [Fact]
    public async Task Editing_a_copy_offers_nothing()
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(bookId, "BK-1");

        await _service.UpdateCopyAsync(bookId, copy.Id, new UpdateCopyRequest(BookCopyCondition.Fair, "B-02"));

        Assert.Empty(_queue.Offers);
    }

    [Fact]
    public async Task The_answer_shows_a_new_copy_reserved_by_the_queue()
    {
        Guid bookId = _repository.AddBook();
        _queue.OnOffer = ReserveOfferedCopyAsync;

        var copy = await _service.AddCopyAsync(bookId, NewCopy("BK-1"));

        Assert.Equal(BookCopyStatus.Reserved, copy.Status);
        Assert.Equal(BookCopyStatus.Reserved, _repository.Copies.Single().Status);
    }

    [Fact]
    public async Task The_answer_shows_a_repaired_copy_reserved_by_the_queue()
    {
        Guid bookId = _repository.AddBook();
        var copy = _repository.AddCopy(bookId, "BK-1", BookCopyStatus.UnderRepair);
        _queue.OnOffer = ReserveOfferedCopyAsync;

        var result = await _service.ChangeStatusAsync(bookId, copy.Id, new ChangeCopyStatusRequest(BookCopyStatus.Available));

        Assert.Equal(BookCopyStatus.Reserved, result.Status);
        Assert.Equal(BookCopyStatus.Reserved, copy.Status);
    }

    /// <summary>What Borrowing does when someone is waiting: the offered copy becomes Reserved.</summary>
    private async Task ReserveOfferedCopyAsync(Guid copyId)
    {
        BookCopy copy = (await _repository.GetByIdAsync(copyId, CancellationToken.None))!;
        copy.ChangeStatus(BookCopyStatus.Reserved);
    }

    private static AddCopyRequest NewCopy(string barcode) => new(barcode, BookCopyCondition.Good, null);

    private static async Task<Error> ErrorOf(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<DomainException>(action);
        return exception.Error;
    }
}
