using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// The real Borrowing read contract for Staff (BR-MEM-4) on the host with the Development seed: the registered
/// provider, the open loans it returns, and Staff's deactivation refused because of them.
/// </summary>
public class BorrowingReadServiceTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    /// <summary>An Active loan (first book), an Overdue one (second book) and a returned one (third book, not open).</summary>
    private async Task<(Loan Active, Loan Overdue)> SeedLoansAsync(Guid member, int activeBook, int overdueBook, int returnedBook)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var active = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(activeBook, 1), member, Now.AddDays(-2), 14);
        var overdue = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(overdueBook, 1), member, Now.AddDays(-30), 14);
        overdue.MarkOverdue(Now);
        var returned = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(returnedBook, 1), member, Now.AddDays(-40), 14);
        returned.Return(Now.AddDays(-30));

        db.Loans.AddRange(active, overdue, returned);
        await db.SaveChangesAsync();
        return (active, overdue);
    }

    [Fact]
    public async Task The_application_uses_the_real_provider_and_returns_only_open_loans_with_their_books()
    {
        var (active, overdue) = await SeedLoansAsync(Members.DavitHakobyan, 13, 14, 7);

        using var scope = factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IBorrowingReadService>();
        Assert.IsType<BorrowingReadService>(reader);

        var loans = await reader.GetActiveLoansAsync(Members.DavitHakobyan);

        // Earliest due date first; the returned loan is not open.
        Assert.Equal(
            [
                new OpenLoanInfo(overdue.Id, Members.DavitHakobyan, CatalogSeedIds.Copy(14, 1), CatalogSeedIds.Book(14), overdue.DueAtUtc, LoanStatus.Overdue),
                new OpenLoanInfo(active.Id, Members.DavitHakobyan, CatalogSeedIds.Copy(13, 1), CatalogSeedIds.Book(13), active.DueAtUtc, LoanStatus.Active),
            ],
            loans);
        Assert.Empty(await reader.GetActiveLoansAsync(Members.MariamAvetisyan));
    }

    [Fact]
    public async Task Deactivating_a_staff_member_with_open_loans_is_refused_with_the_outstanding_books()
    {
        await SeedLoansAsync(Members.GorMkrtchyan, 16, 17, 18);
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
            new LoginAccount(Guid.NewGuid(), "admin@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active,
                [StaffRoles.LibraryAdministrator])).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync($"/api/staff-members/{Members.GorMkrtchyan}/deactivate",
            new DeactivateStaffMemberRequest("Left the bank"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("staff.has_active_loans", body.GetProperty("code").GetString());
        var barcodes = body.GetProperty("outstandingBooks").EnumerateArray()
            .Select(book => book.GetProperty("barcode").GetString()).ToHashSet();
        Assert.Equal([CatalogSeedIds.CopyBarcode(16, 1), CatalogSeedIds.CopyBarcode(17, 1)], barcodes.Order());
    }
}
