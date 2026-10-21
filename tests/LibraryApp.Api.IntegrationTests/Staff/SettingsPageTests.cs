using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

public sealed class SettingsPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Path = "/settings";

    [Fact]
    public async Task Administrator_sees_department_and_category_settings()
    {
        using var client = await ClientAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);
        using var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Departments", html);
        Assert.Contains("Staff categories", html);
        Assert.Contains("Information Technology", html);
        Assert.Contains("Employee", html);
        Assert.Contains("Books", html);
        Assert.Contains("Days", html);
        Assert.Contains("Renewals", html);
        Assert.Contains("Edit", html);
        Assert.Contains("staff", html);
        Assert.DoesNotContain("Delete department", html);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }

    [Fact]
    public async Task Librarian_gets_forbidden_from_settings()
    {
        using var client = await ClientAsync(StaffSeedIds.StaffMembers.AramSargsyan);
        using var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location!.AbsolutePath);
        using var forbidden = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Adding_a_duplicate_department_shows_a_conflict_error_and_preserves_the_name()
    {
        using var client = await ClientAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);
        using var page = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = Regex.Match(await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        using var scope = factory.Services.CreateScope();
        var existing = (await scope.ServiceProvider.GetRequiredService<IDepartmentService>().ListAsync()).First();
        var fields = new Dictionary<string, string>
        {
            ["Name"] = existing.Name,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, Path + "?handler=CreateDepartment")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.Add("Cookie", string.Join("; ",
            client.DefaultRequestHeaders.GetValues("Cookie")) + "; " + UiTestClient.SetCookies(page));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("A department with this name already exists.", html);
        Assert.Contains("alert-red", html);
        Assert.Contains($"value=\"{existing.Name}\"", html);
        Assert.DoesNotContain("Department added.", html);
    }

    [Fact]
    public async Task Department_can_be_renamed()
    {
        using var client = await ClientAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDepartmentService>();
        var department = (await service.ListAsync()).First();
        var newName = $"Renamed {Guid.NewGuid():N}";

        using var response = await PostPageAsync(client, "RenameDepartment", new Dictionary<string, string>
        {
            ["id"] = department.Id.ToString(),
            ["Name"] = newName,
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(newName, (await service.GetAsync(department.Id)).Name);
    }

    [Fact]
    public async Task Category_can_be_added_and_updated()
    {
        using var client = await ClientAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);
        var name = $"Test category {Guid.NewGuid():N}";

        using (var create = await PostPageAsync(client, "CreateCategory", new Dictionary<string, string>
        {
            ["Name"] = name,
            ["MaxLoans"] = "4",
            ["LoanDays"] = "18",
            ["MaxRenewals"] = "2",
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        }

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IStaffCategoryService>();
        var created = (await service.GetListAsync()).Single(item => item.Name == name);

        using var update = await PostPageAsync(client, "UpdateCategory", new Dictionary<string, string>
        {
            ["id"] = created.Id.ToString(),
            ["Name"] = name + " Updated",
            ["MaxLoans"] = "6",
            ["LoanDays"] = "24",
            ["MaxRenewals"] = "3",
        });

        Assert.Equal(HttpStatusCode.Redirect, update.StatusCode);
        var updated = await service.GetAsync(created.Id);
        Assert.Equal(name + " Updated", updated.Name);
        Assert.Equal(6, updated.MaxLoans);
        Assert.Equal(24, updated.LoanDays);
        Assert.Equal(3, updated.MaxRenewals);
    }

    [Fact]
    public async Task Adding_a_duplicate_category_shows_a_field_error()
    {
        using var client = await ClientAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);
        using var scope = factory.Services.CreateScope();
        var existing = (await scope.ServiceProvider.GetRequiredService<IStaffCategoryService>().GetListAsync()).First();

        using var response = await PostPageAsync(client, "CreateCategory", new Dictionary<string, string>
        {
            ["Name"] = existing.Name,
            ["MaxLoans"] = "3",
            ["LoanDays"] = "21",
            ["MaxRenewals"] = "1",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Staff category name is already in use.", html);
        Assert.Contains("aria-invalid=\"true\"", html);
    }

    [Theory]
    [InlineData("0", "21", "Maximum loans must be greater than zero.")]
    [InlineData("3", "0", "Loan days must be greater than zero.")]
    public async Task Adding_a_category_with_invalid_limits_shows_field_errors(
        string maxLoans, string loanDays, string expectedError)
    {
        using var client = await ClientAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);

        using var response = await PostPageAsync(client, "CreateCategory", new Dictionary<string, string>
        {
            ["Name"] = $"Invalid category {Guid.NewGuid():N}",
            ["MaxLoans"] = maxLoans,
            ["LoanDays"] = loanDays,
            ["MaxRenewals"] = "1",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedError, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Empty_category_name_shows_a_friendly_required_error()
    {
        using var client = await ClientAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);

        using var response = await PostPageAsync(client, "CreateCategory", new Dictionary<string, string>
        {
            ["Name"] = "",
            ["MaxLoans"] = "3",
            ["LoanDays"] = "21",
            ["MaxRenewals"] = "1",
        });

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Staff category name is required.", html);
        Assert.DoesNotContain("The Name field is required.", html);
    }

    private async Task<HttpResponseMessage> PostPageAsync(
        HttpClient client, string handler, Dictionary<string, string> fields)
    {
        using var page = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = Regex.Match(await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Path}?handler={handler}")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.Add("Cookie", string.Join("; ",
            client.DefaultRequestHeaders.GetValues("Cookie")) + "; " + UiTestClient.SetCookies(page));
        return await client.SendAsync(request);
    }

    private Task<HttpClient> ClientAsync(Guid staffMemberId)
    {
        var person = StaffSeedData.People.Single(item => item.StaffMemberId == staffMemberId);
        return UiTestClient.SignedInAsync(factory, person.Email, StaffSeedData.Password);
    }
}
