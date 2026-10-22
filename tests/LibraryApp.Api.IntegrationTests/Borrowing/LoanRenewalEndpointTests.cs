using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// BOR-04, BR-BOR-1, NFR-03 through HTTP on SQL Server: who may renew, every refusal with its own code, the new
/// due date by the category's loan days and <c>LoanRenewed</c> in the outbox. The host has no Borrowing seed, and every
/// test uses its own book, so the tests of the class do not share loans or queues.
/// </summary>
public class LoanRenewalEndpointTests(BorrowingDailyJobHost factory) : IClassFixture<BorrowingDailyJobHost>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Theory]
    [InlineData(StaffRoles.StaffMember, 20)]
    [InlineData(StaffRoles.Librarian, 21)]
    [InlineData(StaffRoles.LibraryAdministrator, 22)]
    public async Task A_renewal_moves_the_due_date_by_the_loan_days_and_publishes_LoanRenewed(string role, int book)
    {
        // Anna Petrosyan is an Employee: 21 loan days, 1 renewal (MEM-04 seed).
        Loan loan = await SeedLoanAsync(book, Members.AnnaPetrosyan);
        int eventsBefore = await EventsAsync<LoanRenewed>(loan.Id);

        using HttpClient client = Client(role);
        using HttpResponseMessage response = await client.PostAsync($"/api/loans/{loan.Id}/renew", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        LoanDto body = (await response.Content.ReadFromJsonAsync<LoanDto>(Json))!;
        Assert.Equal((loan.DueAtUtc.AddDays(21), 1, LoanStatus.Active), (body.DueAtUtc, body.RenewalCount, body.Status));

        Loan saved = await ReadAsync(loan.Id);
        Assert.Equal((loan.DueAtUtc.AddDays(21), 1), (saved.DueAtUtc, saved.RenewalCount));

        Assert.Equal(eventsBefore + 1, await EventsAsync<LoanRenewed>(loan.Id));
        LoanRenewed renewed = await LastEventAsync<LoanRenewed>(loan.Id);
        Assert.Equal(
            (Members.AnnaPetrosyan, CatalogSeedIds.Book(book), saved.DueAtUtc, 1),
            (renewed.StaffMemberId, renewed.BookId, renewed.NewDueAtUtc, renewed.RenewalCount));
    }

    [Fact]
    public async Task A_staff_member_cannot_renew_another_persons_loan_and_gets_404()
    {
        Loan loan = await SeedLoanAsync(23, Members.MariamAvetisyan);

        using HttpClient anna = Client(StaffRoles.StaffMember);
        await AssertProblemAsync(await anna.PostAsync($"/api/loans/{loan.Id}/renew", null),
            HttpStatusCode.NotFound, BorrowingErrors.LoanNotFound.Code);

        Assert.Equal(0, (await ReadAsync(loan.Id)).RenewalCount);
    }

    [Fact]
    public async Task Anonymous_and_unknown_loans_are_refused()
    {
        using HttpClient anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/api/loans/{Guid.NewGuid()}/renew", null)).StatusCode);

        using HttpClient librarian = Client(StaffRoles.Librarian);
        await AssertProblemAsync(await librarian.PostAsync($"/api/loans/{Guid.NewGuid()}/renew", null),
            HttpStatusCode.NotFound, BorrowingErrors.LoanNotFound.Code);
    }

    [Theory]
    [InlineData("overdue")]
    [InlineData("past-due")]
    public async Task An_overdue_loan_cannot_be_renewed(string state)
    {
        DateTime now = Now;
        // Past due before the daily job marked it Overdue (NextLocalDay: the due date was two days ago) counts too.
        Loan loan = await SeedLoanAsync(state == "overdue" ? 24 : 25, Members.AnnaPetrosyan,
            issuedAtUtc: now.AddDays(-23), loanDays: 21, prepare: state == "overdue" ? l => l.MarkOverdue(now) : null);

        await AssertRefusedAsync(loan, BorrowingErrors.LoanOverdueCannotBeRenewed.Code);
    }

    [Theory]
    [InlineData(26, "restricted")]
    [InlineData(27, "suspended")]
    public async Task A_restricted_or_suspended_member_cannot_renew(int book, string state)
    {
        // Gor Mkrtchyan is seeded Restricted, Sona Vardanyan Suspended (BR-MEM-3; SuspendedMayRenew = false).
        Guid member = state == "restricted" ? Members.GorMkrtchyan : Members.SonaVardanyan;
        Loan loan = await SeedLoanAsync(book, member);

        await AssertRefusedAsync(loan, state == "restricted"
            ? BorrowingErrors.RestrictedRenewalForbidden.Code
            : BorrowingErrors.SuspendedRenewalForbidden.Code);
    }

    [Fact]
    public async Task A_loan_at_the_maximum_renewals_is_refused_with_the_acceptance_text()
    {
        // Employee: MaxRenewals = 1; the loan was renewed once already.
        Loan loan = await SeedLoanAsync(28, Members.AnnaPetrosyan, prepare: l => l.Renew(21));

        using HttpClient librarian = Client(StaffRoles.Librarian);
        using HttpResponseMessage response = await librarian.PostAsync($"/api/loans/{loan.Id}/renew", null);

        JsonElement problem = await AssertProblemAsync(response,
            HttpStatusCode.UnprocessableEntity, BorrowingErrors.MaxRenewalsReached.Code);
        Assert.Contains("Maximum renewals reached", problem.GetProperty("detail").GetString());
        Assert.Equal(1, (await ReadAsync(loan.Id)).RenewalCount);
    }

    [Fact]
    public async Task A_waiting_reservation_of_another_member_blocks_the_renewal()
    {
        Loan loan = await SeedLoanAsync(29, Members.AnnaPetrosyan);
        await Db(async db =>
        {
            db.Reservations.Add(Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(29), Members.MariamAvetisyan, Now, 1));
            return await db.SaveChangesAsync();
        });

        await AssertRefusedAsync(loan, BorrowingErrors.RenewalBlockedByReservation.Code);
    }

    [Fact]
    public async Task A_returned_loan_cannot_be_renewed()
    {
        Loan loan = await SeedLoanAsync(30, Members.AnnaPetrosyan, prepare: l => l.Return(Now));

        await AssertRefusedAsync(loan, BorrowingErrors.LoanCannotBeRenewed.Code);
    }

    private async Task AssertRefusedAsync(Loan loan, string code)
    {
        int eventsBefore = await EventsAsync<LoanRenewed>(loan.Id);

        using HttpClient librarian = Client(StaffRoles.Librarian);
        await AssertProblemAsync(await librarian.PostAsync($"/api/loans/{loan.Id}/renew", null),
            HttpStatusCode.UnprocessableEntity, code);

        Loan saved = await ReadAsync(loan.Id);
        Assert.Equal((loan.DueAtUtc, loan.RenewalCount), (saved.DueAtUtc, saved.RenewalCount));
        Assert.Equal(eventsBefore, await EventsAsync<LoanRenewed>(loan.Id));
    }

    private DateTime Now => factory.Services.GetRequiredService<IClock>().UtcNow;

    /// <summary>A loan of the first copy of the seeded book, issued yesterday for 21 days unless told otherwise.</summary>
    private async Task<Loan> SeedLoanAsync(
        int book,
        Guid staffMemberId,
        DateTime? issuedAtUtc = null,
        int loanDays = 21,
        Action<Loan>? prepare = null)
    {
        Guid copyId = CatalogSeedIds.Copy(book, 1);
        Loan loan = Loan.Issue(Guid.NewGuid(), copyId, staffMemberId, issuedAtUtc ?? Now.AddDays(-1), loanDays);
        prepare?.Invoke(loan);

        await Db(async db =>
        {
            BookCopy copy = await db.BookCopies.SingleAsync(c => c.Id == copyId);
            if (loan.IsOpen && copy.Status != BookCopyStatus.OnLoan)
                copy.ChangeStatus(BookCopyStatus.OnLoan);
            db.Loans.Add(loan);
            return await db.SaveChangesAsync();
        });

        return loan;
    }

    private Task<Loan> ReadAsync(Guid loanId) =>
        Db(db => db.Loans.AsNoTracking().SingleAsync(loan => loan.Id == loanId));

    private async Task<int> EventsAsync<T>(Guid loanId) => (await PayloadsAsync<T>()).Count(payload => payload.Contains(loanId.ToString()));

    private async Task<T> LastEventAsync<T>(Guid loanId) =>
        JsonSerializer.Deserialize<T>((await PayloadsAsync<T>()).Last(payload => payload.Contains(loanId.ToString())), Json)!;

    private Task<List<string>> PayloadsAsync<T>() => Db(db => db.OutboxMessages.AsNoTracking()
        .Where(message => message.Type == typeof(T).Name)
        .OrderBy(message => message.OccurredAtUtc)
        .Select(message => message.Payload)
        .ToListAsync());

    private async Task<T> Db<T>(Func<LibraryDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
    }

    private HttpClient Client(string role)
    {
        HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(factory.Account(role)).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }
}
