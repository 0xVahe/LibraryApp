using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>Next expected return date for Catalog (CAT-08) on the host with the Development seed.</summary>
public class LoanQueryServiceTests(ApiHostWithoutBorrowingSeed factory) : IClassFixture<ApiHostWithoutBorrowingSeed>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private async Task AddAsync(params Loan[] loans)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        db.Loans.AddRange(loans);
        await db.SaveChangesAsync();
    }

    private async Task<DateTime?> NextDueDateAsync(int book)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILoanQueryService>().GetNextDueDateAsync(CatalogSeedIds.Book(book));
    }

    [Fact]
    public void The_application_uses_the_real_provider()
    {
        using var scope = factory.Services.CreateScope();
        Assert.IsType<LoanQueryService>(scope.ServiceProvider.GetRequiredService<ILoanQueryService>());
    }

    [Fact]
    public async Task A_book_without_open_loans_has_no_next_due_date()
    {
        var returned = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(20, 1), Members.AnnaPetrosyan, Now.AddDays(-30), 21);
        returned.Return(Now.AddDays(-10));
        var lost = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(20, 2), Members.AnnaPetrosyan, Now.AddDays(-30), 21);
        lost.MarkLost(Now.AddDays(-5));
        await AddAsync(returned, lost);

        Assert.Null(await NextDueDateAsync(20));
        Assert.Null(await NextDueDateAsync(29));   // never lent
    }

    [Fact]
    public async Task The_earliest_due_date_of_the_open_loans_of_the_book_wins_an_overdue_one_included()
    {
        // Book 21 has 3 copies: one Active, one Overdue (its due date is the earliest), one free.
        var active = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(21, 1), Members.MariamAvetisyan, Now.AddDays(-2), 30);
        var overdue = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(21, 2), Members.DavitHakobyan, Now.AddDays(-20), 14);
        overdue.MarkOverdue(Now);
        // An earlier due date on another book must not count.
        var otherBook = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(22, 1), Members.DavitHakobyan, Now.AddDays(-40), 14);
        await AddAsync(active, overdue, otherBook);

        Assert.Equal(overdue.DueAtUtc, await NextDueDateAsync(21));
        Assert.Equal(otherBook.DueAtUtc, await NextDueDateAsync(22));
    }
}
