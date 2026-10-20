using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

public sealed class ConfiguredStaffRulesFactory : ApiHostFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Staff:Import:MaxRows", "2");
        builder.UseSetting("Staff:Import:MaxFileBytes", "2048");
        builder.UseSetting("Staff:RestrictedLimits:MaxLoans", "2");
        builder.UseSetting("Staff:RestrictedLimits:MaxRenewals", "1");
    }
}

public sealed class ConfiguredStaffRulesTests(ConfiguredStaffRulesFactory factory) : IClassFixture<ConfiguredStaffRulesFactory>
{
    private const string ImportPage = "/staff/import";

    private HttpClient ApiClient()
    {
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(factory.Account(StaffRoles.Librarian));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    [Fact]
    public async Task Api_upload_errors_report_the_configured_size()
    {
        using var client = ApiClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[2049]), "file", "staff.csv");

        using var response = await client.PostAsync("/api/staff-members/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("staff.import_invalid", body.GetProperty("code").GetString());
        Assert.Contains("2048 bytes", body.GetProperty("rows")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Api_parser_rejects_rows_using_the_same_configured_limit()
    {
        const string header = "EmployeeId,FirstName,LastName,CorporateEmail,Department,Position,LineManagerEmail,Category\n";
        const string row = "EMP-1,Anna,Employee,anna@ameriabank.am,IT,Engineer,,Employee\n";
        using var client = ApiClient();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(header + row + row + row), "file", "staff.csv");

        using var response = await client.PostAsync("/api/staff-members/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("at most 2 data rows", body.GetProperty("rows")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Razor_form_and_post_use_the_configured_limits_with_antiforgery()
    {
        using var client = await UiTestClient.SignedInAsync(factory,
            LoginPage.Email(StaffSeedIds.StaffMembers.AramSargsyan), StaffSeedData.Password);
        using var page = await client.GetAsync(ImportPage);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Up to 2 data rows, 2048 bytes.", html);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var cookies = string.Join("; ", client.DefaultRequestHeaders.GetValues("Cookie")) + "; " + UiTestClient.SetCookies(page);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", cookies);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(WebUtility.HtmlDecode(token)), "__RequestVerificationToken");
        content.Add(new ByteArrayContent(new byte[2049]), "CsvFile", "staff.csv");

        using var response = await client.PostAsync(ImportPage, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The file must be 2048 bytes or smaller.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Restricted_lending_rules_use_the_host_configuration()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IStaffMemberService>();
        var memberId = StaffSeedIds.StaffMembers.AnnaPetrosyan;
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(ct =>
            service.SetStatusAsync(memberId, StaffMemberStatus.Restricted, "Configured limit test", ct));
        var rules = await service.GetLendingRulesAsync(memberId);
        Assert.Equal(2, rules.MaxLoans);
        Assert.Equal(1, rules.MaxRenewals);
    }
}
