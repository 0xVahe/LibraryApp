using System.Net;
using System.Net.Http.Headers;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Api.IntegrationTests.Reporting;

public sealed class ReportingCsvEndpointTests(ApiHostFactory factory)
    : IClassFixture<ApiHostFactory>
{
    private static readonly string[] Routes =
    [
        "/api/reports/overdue-loans/export",
        "/api/reports/dashboard/export",
        "/api/reports/top-books-and-categories/export?from=2026-01-01&to=2026-12-31",
        "/api/reports/reading-activity-by-department/export?from=2026-01-01&to=2026-12-31"
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Anonymous_export_requests_return_401(int routeIndex)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Routes[routeIndex]);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Staff_member_export_requests_return_403(int routeIndex)
    {
        using var client = CreateAuthenticatedClient(StaffRoles.StaffMember);
        using var response = await client.GetAsync(Routes[routeIndex]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Authorized_roles_can_export_csv(int routeIndex)
    {
        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);
        using var response = await client.GetAsync(Routes[routeIndex]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".csv",
            response.Content.Headers.ContentDisposition?.FileNameStar ??
            response.Content.Headers.ContentDisposition?.FileName);

        string csv = await response.Content.ReadAsStringAsync();

        Assert.NotEmpty(csv);
        Assert.Contains("GeneratedAtUtc", csv);
    }

    [Fact]
    public async Task Administrator_can_export_department_reading_activity()
    {
        using var client = CreateAuthenticatedClient(StaffRoles.LibraryAdministrator);
        using var response = await client.GetAsync(Routes[3]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Librarian_cannot_export_department_reading_activity()
    {
        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);
        using var response = await client.GetAsync(Routes[3]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/reports/overdue-loans/export?minimumDaysOverdue=-1")]
    [InlineData("/api/reports/overdue-loans/export?departmentId=invalid")]
    [InlineData("/api/reports/top-books-and-categories/export?from=2026-12-31&to=2026-01-01")]
    public async Task Invalid_export_filters_return_400(string route)
    {
        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_export_contains_expected_csv_columns_and_data()
    {
        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);
        using var response = await client.GetAsync("/api/reports/dashboard/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string csv = await response.Content.ReadAsStringAsync();

        string[] lines = csv.TrimStart('\uFEFF')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal(
            "\"AsOfDate\",\"GeneratedAtUtc\",\"TotalBooks\",\"TotalCopies\",\"ActiveReaders\",\"ActiveLoans\",\"OverdueLoans\",\"OverduePercentage\",\"RestrictedEmployees\",\"SuspendedEmployees\"",
            lines[0]);

        Assert.Equal(10, lines[1].Count(c => c == '"') / 2);
    }

    [Fact]
    public async Task Overdue_loans_export_contains_all_report_rows()
    {
        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);

        using var reportResponse = await client.GetAsync(
            "/api/reports/overdue-loans?page=1&pageSize=1");

        using var exportResponse = await client.GetAsync(
            "/api/reports/overdue-loans/export");

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);

        string csv = await exportResponse.Content.ReadAsStringAsync();

        Assert.StartsWith("\"AsOfDate\"", csv.TrimStart('\uFEFF'));
        Assert.Contains("\"LoanId\"", csv);
    }

    [Fact]
    public async Task Overdue_loans_export_includes_more_than_100_rows_without_duplicates()
    {
        const int count = 101;

        var now = factory.Services.GetRequiredService<IClock>().UtcNow;
        var staffMemberId = factory.Account(StaffRoles.Librarian).StaffMemberId!.Value;

        var expectedIds = new HashSet<Guid>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

            var book = await db.Books.AsNoTracking().FirstAsync();

            for (int i = 0; i < count; i++)
            {
                var copyId = Guid.NewGuid();
                var loanId = Guid.NewGuid();

                db.BookCopies.Add(new BookCopy(
                    copyId,
                    book.Id,
                    $"CSV-REP08-{i:D3}",
                    BookCopyCondition.Good,
                    null));

                db.Loans.Add(Loan.Issue(
                    loanId,
                    copyId,
                    staffMemberId,
                    now.AddDays(-60),
                    21));

                expectedIds.Add(loanId);
            }

            await db.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);

        using var jsonResponse = await client.GetAsync(
            "/api/reports/overdue-loans?page=1&pageSize=100");

        using var csvResponse = await client.GetAsync(
            "/api/reports/overdue-loans/export");

        Assert.Equal(HttpStatusCode.OK, jsonResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, csvResponse.StatusCode);

        var report = await jsonResponse.Content.ReadFromJsonAsync<OverdueLoansReportDto>();

        Assert.NotNull(report);
        Assert.True(report.TotalCount >= count);

        var csv = await csvResponse.Content.ReadAsStringAsync();

        var lines = csv.TrimStart('\uFEFF')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(report.TotalCount + 1, lines.Length);

        var csvIds = lines.Skip(1)
            .Select(line => Guid.Parse(line.Split(',')[2].Trim('"')))
            .ToHashSet();

        Assert.Equal(report.TotalCount, csvIds.Count);
        Assert.True(expectedIds.IsSubsetOf(csvIds));
    }

    private HttpClient CreateAuthenticatedClient(string role)
    {
        var client = factory.CreateClient();

        var token = factory.Services
            .GetRequiredService<IAccessTokenIssuer>()
            .Issue(factory.Account(role))
            .Token;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}