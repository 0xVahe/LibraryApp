using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// BOR-10, NFR-03 through HTTP on SQL Server: the loan history per staff member and per book, with the status
/// filter and paging, newest first; a Staff Member gets only their own loans. The class seeds its loans once on a host
/// without the Borrowing seed.
/// </summary>
public class LoanHistoryEndpointTests(LoanHistoryEndpointTests.Host factory) : IClassFixture<LoanHistoryEndpointTests.Host>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Tigran: 3 loans (book 41 returned, book 42 overdue, book 41 again active, newest); Anna: 1 loan of book 42.
    /// </summary>
    public sealed class Host : ApiHostFactory, IAsyncLifetime
    {
        public Loan TigranOld { get; private set; } = null!;
        public Loan TigranOverdue { get; private set; } = null!;
        public Loan TigranNew { get; private set; } = null!;
        public Loan Anna { get; private set; } = null!;

        /// <summary>Davit: one Active loan due 3 days ago that the daily job has not marked Overdue yet.</summary>
        public Loan DavitPastDue { get; private set; } = null!;

        // The class owns every loan: no Borrowing seed.
        protected override void ConfigureTestServices(IServiceCollection services) =>
            services.Remove(services.Single(d => d.ServiceType == typeof(IModuleSeeder) && d.ImplementationType == typeof(BorrowingSeeder)));

        Task IAsyncLifetime.DisposeAsync() => DisposeAsync();

        async Task IAsyncLifetime.InitializeAsync()
        {
            await InitializeAsync();

            DateTime now = Services.GetRequiredService<IClock>().UtcNow;
            TigranOld = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(41, 1), Members.TigranMelikyan, now.AddDays(-60), 14);
            TigranOld.Return(now.AddDays(-50));
            TigranOverdue = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(42, 1), Members.TigranMelikyan, now.AddDays(-30), 14);
            TigranOverdue.MarkOverdue(now);
            TigranNew = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(41, 2), Members.TigranMelikyan, now.AddDays(-2), 14);
            Anna = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(42, 2), Members.AnnaPetrosyan, now.AddDays(-1), 21);
            DavitPastDue = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(43, 1), Members.DavitHakobyan, now.AddDays(-17), 14);

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            db.Loans.AddRange(TigranOld, TigranOverdue, TigranNew, Anna, DavitPastDue);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task The_history_of_a_staff_member_lists_every_status_newest_first()
    {
        PagedResult<LoanHistoryItemDto> page = await GetAsync(StaffRoles.Librarian, $"staffMemberId={Members.TigranMelikyan}");

        Assert.Equal(
            new[] { factory.TigranNew.Id, factory.TigranOverdue.Id, factory.TigranOld.Id },
            page.Items.Select(item => item.LoanId));
        Assert.Equal((3, 1, PageRequest.DefaultPageSize), (page.TotalCount, page.Page, page.PageSize));

        LoanHistoryItemDto returned = page.Items.Single(item => item.LoanId == factory.TigranOld.Id);
        Assert.Equal((LoanStatus.Returned, factory.TigranOld.ReturnedAtUtc), (returned.Status, returned.ReturnedAtUtc));
    }

    [Fact]
    public async Task The_history_of_a_book_covers_all_its_copies_and_staff_members()
    {
        PagedResult<LoanHistoryItemDto> book41 = await GetAsync(StaffRoles.Librarian, $"bookId={CatalogSeedIds.Book(41)}");
        Assert.Equal(new[] { factory.TigranNew.Id, factory.TigranOld.Id }, book41.Items.Select(item => item.LoanId));

        PagedResult<LoanHistoryItemDto> book42 = await GetAsync(StaffRoles.Librarian, $"bookId={CatalogSeedIds.Book(42)}");
        Assert.Equal(new[] { factory.Anna.Id, factory.TigranOverdue.Id }, book42.Items.Select(item => item.LoanId));

        PagedResult<LoanHistoryItemDto> unknown = await GetAsync(StaffRoles.Librarian, $"bookId={Guid.NewGuid()}");
        Assert.Empty(unknown.Items);
    }

    [Fact]
    public async Task The_status_filter_and_the_paging_narrow_the_list()
    {
        PagedResult<LoanHistoryItemDto> overdue = await GetAsync(
            StaffRoles.LibraryAdministrator, $"staffMemberId={Members.TigranMelikyan}&status=Overdue");
        Assert.Equal(factory.TigranOverdue.Id, Assert.Single(overdue.Items).LoanId);

        PagedResult<LoanHistoryItemDto> second = await GetAsync(
            StaffRoles.Librarian, $"staffMemberId={Members.TigranMelikyan}&page=2&pageSize=2");
        Assert.Equal(factory.TigranOld.Id, Assert.Single(second.Items).LoanId);
        Assert.Equal((3, 2, 2), (second.TotalCount, second.Page, second.PageSize));

        // Page below 1 is page 1, the page size is at most 100.
        PagedResult<LoanHistoryItemDto> clamped = await GetAsync(
            StaffRoles.Librarian, $"staffMemberId={Members.TigranMelikyan}&page=0&pageSize=1000");
        Assert.Equal((1, PageRequest.MaxPageSize, 3), (clamped.Page, clamped.PageSize, clamped.Items.Count));
    }

    [Fact]
    public async Task An_active_loan_past_its_due_date_is_overdue_in_the_filter_and_the_result_before_the_daily_job()
    {
        string davit = $"staffMemberId={Members.DavitHakobyan}";

        PagedResult<LoanHistoryItemDto> overdue = await GetAsync(StaffRoles.Librarian, $"{davit}&status=Overdue");
        LoanHistoryItemDto item = Assert.Single(overdue.Items);
        Assert.Equal((factory.DavitPastDue.Id, LoanStatus.Overdue), (item.LoanId, item.Status));

        PagedResult<LoanHistoryItemDto> active = await GetAsync(StaffRoles.Librarian, $"{davit}&status=Active");
        Assert.Empty(active.Items);
        Assert.Equal(0, active.TotalCount);

        PagedResult<LoanHistoryItemDto> all = await GetAsync(StaffRoles.Librarian, davit);
        Assert.Equal(LoanStatus.Overdue, Assert.Single(all.Items).Status);

        // An Active loan that is not due yet stays Active in both.
        PagedResult<LoanHistoryItemDto> tigranActive = await GetAsync(StaffRoles.Librarian, $"staffMemberId={Members.TigranMelikyan}&status=Active");
        Assert.Equal((factory.TigranNew.Id, LoanStatus.Active), (Assert.Single(tigranActive.Items).LoanId, tigranActive.Items[0].Status));
    }

    [Fact]
    public async Task The_issue_period_keeps_the_loans_issued_on_its_local_days_both_included()
    {
        IClock clock = factory.Services.GetRequiredService<IClock>();
        string Day(Loan loan) => clock.ToLocalDate(loan.IssuedAtUtc).ToString("yyyy-MM-dd");

        PagedResult<LoanHistoryItemDto> fromOverdue = await GetAsync(StaffRoles.Librarian,
            $"staffMemberId={Members.TigranMelikyan}&issuedFrom={Day(factory.TigranOverdue)}");
        Assert.Equal(new[] { factory.TigranNew.Id, factory.TigranOverdue.Id }, fromOverdue.Items.Select(item => item.LoanId));

        PagedResult<LoanHistoryItemDto> toOverdue = await GetAsync(StaffRoles.Librarian,
            $"staffMemberId={Members.TigranMelikyan}&issuedTo={Day(factory.TigranOverdue)}");
        Assert.Equal(new[] { factory.TigranOverdue.Id, factory.TigranOld.Id }, toOverdue.Items.Select(item => item.LoanId));

        PagedResult<LoanHistoryItemDto> oneDay = await GetAsync(StaffRoles.Librarian,
            $"staffMemberId={Members.TigranMelikyan}&issuedFrom={Day(factory.TigranOld)}&issuedTo={Day(factory.TigranOld)}");
        Assert.Equal(factory.TigranOld.Id, Assert.Single(oneDay.Items).LoanId);
        Assert.Equal(1, oneDay.TotalCount);
    }

    [Fact]
    public async Task An_issue_period_that_ends_before_it_starts_is_refused()
    {
        using HttpClient librarian = Client(StaffRoles.Librarian);

        using HttpResponseMessage response = await librarian.GetAsync("/api/loans?issuedFrom=2026-10-10&issuedTo=2026-10-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(BorrowingErrors.HistoryPeriodInvalid.Code, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("staffMemberId=20000000-0000-0000-0003-000000000011")]   // asks for Tigran's loans
    public async Task A_staff_member_gets_only_their_own_loans_whatever_the_filter(string query)
    {
        PagedResult<LoanHistoryItemDto> page = await GetAsync(StaffRoles.StaffMember, query);

        Assert.Equal(factory.Anna.Id, Assert.Single(page.Items).LoanId);
        Assert.All(page.Items, item => Assert.Equal(Members.AnnaPetrosyan, item.StaffMemberId));
    }

    [Fact]
    public async Task An_unknown_status_is_refused_and_anonymous_callers_get_401()
    {
        using HttpClient librarian = Client(StaffRoles.Librarian);
        Assert.Equal(HttpStatusCode.BadRequest, (await librarian.GetAsync("/api/loans?status=Lent")).StatusCode);

        using HttpClient anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/loans")).StatusCode);
    }

    private async Task<PagedResult<LoanHistoryItemDto>> GetAsync(string role, string query)
    {
        using HttpClient client = Client(role);
        using HttpResponseMessage response = await client.GetAsync($"/api/loans?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<LoanHistoryItemDto>>(Json))!;
    }

    private HttpClient Client(string role)
    {
        HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(factory.Account(role)).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
