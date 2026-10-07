using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Tests.Borrowing;

public sealed class LoanServiceTests
{
    [Fact]
    public async Task IssueAsync_creates_loan_and_marks_copy_on_loan()
    {
        // Arrange: an eligible employee and an available copy.
        var staffId = Guid.NewGuid();
        var copyId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

        var staff = new FakeStaffMemberService();
        var loans = new FakeLoanRepository();
        var catalog = new InMemoryCatalogService();
        var clock = new TestClock(now);
        var ids = new FakeIdGenerator();
        var unitOfWork = new FakeUnitOfWork(loans, catalog);

        catalog.Seed(new BookCopyDto(
            copyId,
            Guid.NewGuid(),
            "BK-001",
            BookCopyStatus.Available,
            BookCopyCondition.Good,
            "A-1"));

        var service = new LoanService(
            staff, catalog, loans, unitOfWork, clock, ids);

        // Act.
        LoanDto result = await service.IssueAsync(
            new IssueLoanRequest(staffId, "BK-001"));

        // Assert: the returned data and saved loan are correct.
        Assert.Equal(ids.Id, result.LoanId);
        Assert.Equal(staffId, result.StaffMemberId);
        Assert.Equal(copyId, result.BookCopyId);
        Assert.Equal(now, result.IssuedAtUtc);
        Assert.Equal(now.AddDays(staff.Rules.LoanDays), result.DueAtUtc);
        Assert.Equal(LoanStatus.Active, result.Status);
        Assert.Equal(0, result.RenewalCount);

        Loan saved = Assert.Single(loans.Saved);
        Assert.Equal(result.LoanId, saved.Id);
        Assert.Equal(result.DueAtUtc, saved.DueAtUtc);
        Assert.Empty(loans.Pending);

        BookCopyDto? copy =
            await catalog.GetCopyByBarcodeAsync("BK-001");

        Assert.NotNull(copy);
        Assert.Equal(BookCopyStatus.OnLoan, copy.Status);
        Assert.Equal(1, unitOfWork.CommittedCount);
        Assert.Equal(0, unitOfWork.RolledBackCount);
    }
}