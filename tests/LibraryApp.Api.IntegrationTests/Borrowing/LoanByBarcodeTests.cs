using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// The Loan desk finds the open loan of a copy by its barcode (BOR-03), through the real Catalog lookup and SQL
/// Server. Every test uses its own copies.
/// </summary>
public class LoanByBarcodeTests(ApiHostWithoutBorrowingSeed factory) : IClassFixture<ApiHostWithoutBorrowingSeed>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private async Task SaveAsync(params Loan[] loans)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        db.Loans.AddRange(loans);
        await db.SaveChangesAsync();
    }

    private async Task<LoanDto> FindAsync(string barcode)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILoanService>().FindOpenByBarcodeAsync(barcode);
    }

    private async Task<string> ErrorCodeAsync(string barcode) =>
        (await Assert.ThrowsAsync<DomainException>(() => FindAsync(barcode))).Error.Code;

    [Fact]
    public async Task The_open_loan_is_found_and_an_earlier_returned_loan_of_the_copy_is_ignored()
    {
        var returned = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(50, 1), Members.AnnaPetrosyan, Now.AddDays(-60), 14);
        returned.Return(Now.AddDays(-50));
        var open = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(50, 1), Members.DavitHakobyan, Now.AddDays(-3), 14);
        await SaveAsync(returned, open);

        var loan = await FindAsync(CatalogSeedIds.CopyBarcode(50, 1));

        Assert.Equal(open.Id, loan.LoanId);
        Assert.Equal(Members.DavitHakobyan, loan.StaffMemberId);
        Assert.Equal(LoanStatus.Active, loan.Status);
    }

    [Fact]
    public async Task An_overdue_loan_is_open()
    {
        var overdue = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(51, 1), Members.MariamAvetisyan, Now.AddDays(-30), 14);
        overdue.MarkOverdue(Now);
        await SaveAsync(overdue);

        var loan = await FindAsync(CatalogSeedIds.CopyBarcode(51, 1));

        Assert.Equal(overdue.Id, loan.LoanId);
        Assert.Equal(LoanStatus.Overdue, loan.Status);
    }

    [Fact]
    public async Task The_barcode_is_matched_as_Catalog_does_case_and_surrounding_spaces_ignored()
    {
        var open = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(52, 1), Members.NarekSimonyan, Now.AddDays(-1), 14);
        await SaveAsync(open);

        var loan = await FindAsync($"  {CatalogSeedIds.CopyBarcode(52, 1).ToLowerInvariant()} ");

        Assert.Equal(open.Id, loan.LoanId);
    }

    [Fact]
    public async Task A_returned_copy_has_no_open_loan()
    {
        var returned = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(53, 1), Members.EvaHovsepyan, Now.AddDays(-20), 14);
        returned.Return(Now.AddDays(-2));
        await SaveAsync(returned);

        Assert.Equal(BorrowingErrors.NoOpenLoanForCopy.Code, await ErrorCodeAsync(CatalogSeedIds.CopyBarcode(53, 1)));
        // A copy that was never lent: the same answer.
        Assert.Equal(BorrowingErrors.NoOpenLoanForCopy.Code, await ErrorCodeAsync(CatalogSeedIds.CopyBarcode(54, 1)));
    }

    [Theory]
    [InlineData("BK-UNKNOWN-99")]
    [InlineData("   ")]
    public async Task An_unknown_barcode_is_copy_not_found(string barcode) =>
        Assert.Equal(BorrowingErrors.CopyNotFound.Code, await ErrorCodeAsync(barcode));
}
