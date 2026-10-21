using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Options;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.IntegrationTests.Staff;

public class StaffEnrolmentPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string NewPage = "/staff/new";
    private const string ImportPage = "/staff/import";
    private const string CsvHeader =
        "EmployeeId,FirstName,LastName,CorporateEmail,Department,Position,LineManagerEmail,Category,Role\r\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Library_staff_can_open_the_enrol_form_with_real_lookups(bool administrator)
    {
        using var client = await ClientAsync(administrator);
        var response = await client.GetAsync(NewPage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(StaffSeedIds.Departments.InformationTechnology.ToString(), html);
        Assert.Contains(StaffSeedIds.Categories.Employee.ToString(), html);
        Assert.Contains("Information Technology", html);
        Assert.Contains("Employee", html);
        Assert.Equal(administrator, html.Contains("<option value=\"LibraryAdministrator\"", StringComparison.Ordinal));
        Assert.Contains("value=\"A", InputTag(html, "Input.Password"));
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }

    [Fact]
    public async Task Enrolment_saves_then_redirects_to_an_edit_form_with_a_green_alert_and_profile_link()
    {
        using var client = await ClientAsync();
        var request = Request();
        var response = await PostFormAsync(client, NewPage, Fields(request));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var member = await FindAsync(request.CorporateEmail);
        Assert.Equal($"/staff/{member.Id}/edit", response.Headers.Location!.OriginalString);

        KeepCookies(client, response);
        var page = await client.GetAsync(response.Headers.Location);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("alert-green", html);
        Assert.Contains("Staff member enrolled.", html);
        Assert.Contains($"href=\"/staff/{member.Id}\"", html);
        Assert.Contains(request.EmployeeId, html);
    }

    [Theory]
    [InlineData(true, "Input.EmployeeId", "Employee ID is already enrolled.")]
    [InlineData(false, "Input.CorporateEmail", "Corporate email is already in use.")]
    public async Task A_duplicate_is_a_field_error_and_keeps_the_other_values(bool duplicateId, string field, string message)
    {
        var existing = await MemberAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);
        var request = duplicateId
            ? Request() with { EmployeeId = existing.EmployeeId }
            : Request() with { CorporateEmail = existing.CorporateEmail.ToUpperInvariant() };
        using var client = await ClientAsync();

        var response = await PostFormAsync(client, NewPage, Fields(request));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(message, html);
        Assert.Contains($"data-valmsg-for=\"{field}\"", html);
        Assert.Contains("aria-invalid=\"true\"", InputTag(html, field));
        Assert.Contains($"value=\"{request.FirstName}\"", InputTag(html, "Input.FirstName"));
        Assert.Contains($"value=\"{request.Position}\"", InputTag(html, "Input.Position"));
        Assert.Contains(StaffSeedIds.Departments.InformationTechnology.ToString(), html);
    }

    [Fact]
    public async Task Registered_validation_runs_before_enrolment_and_preserves_the_form()
    {
        var request = Request() with { FirstName = "", LineManagerEmail = "invalid-email" };
        using var client = await ClientAsync();

        var response = await PostFormAsync(client, NewPage, Fields(request));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("aria-invalid=\"true\"", InputTag(html, "Input.FirstName"));
        Assert.Contains("aria-invalid=\"true\"", InputTag(html, "Input.LineManagerEmail"));
        Assert.Contains($"value=\"{request.EmployeeId}\"", InputTag(html, "Input.EmployeeId"));
        Assert.False(await ExistsAsync(request.EmployeeId));
    }

    [Fact]
    public async Task A_librarian_cannot_assign_a_privileged_role_by_changing_the_post()
    {
        var request = Request() with { Role = StaffRoles.LibraryAdministrator };
        using var client = await ClientAsync();

        var response = await PostFormAsync(client, NewPage, Fields(request));

        await AssertForbiddenAsync(client, response);
        Assert.False(await ExistsAsync(request.EmployeeId));
    }

    [Fact]
    public async Task An_administrator_can_assign_the_librarian_role()
    {
        var request = Request() with { Role = StaffRoles.Librarian };
        using var client = await ClientAsync(administrator: true);

        var response = await PostFormAsync(client, NewPage, Fields(request));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(StaffRoles.Librarian, (await FindAsync(request.CorporateEmail)).Role);
    }

    [Fact]
    public async Task Edit_loads_the_profile_and_saves_only_the_supported_fields()
    {
        var member = await CreateMemberAsync();
        using var client = await ClientAsync();
        var path = $"/staff/{member.Id}/edit";
        var before = await client.GetAsync(path);
        var originalHtml = await before.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Contains($"value=\"{member.CorporateEmail}\"", InputTag(originalHtml, "Input.CorporateEmail"));
        Assert.Contains(member.EmployeeId, originalHtml);

        var fields = EditFields(member);
        fields["Input.FirstName"] = "Updated";
        fields["Input.DepartmentId"] = StaffSeedIds.Departments.Finance.ToString();
        fields["Input.StaffCategoryId"] = StaffSeedIds.Categories.ManagerSenior.ToString();
        fields["Input.EmployeeId"] = "TAMPERED";
        fields["Input.Role"] = StaffRoles.LibraryAdministrator;

        var response = await PostFormAsync(client, path, fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var updated = await MemberAsync(member.Id);
        Assert.Equal("Updated", updated.FirstName);
        Assert.Equal(StaffSeedIds.Departments.Finance, updated.DepartmentId);
        Assert.Equal(StaffSeedIds.Categories.ManagerSenior, updated.StaffCategoryId);
        Assert.Equal(member.EmployeeId, updated.EmployeeId);
        Assert.Equal(member.Role, updated.Role);

        KeepCookies(client, response);
        var html = await (await client.GetAsync(response.Headers.Location)).Content.ReadAsStringAsync();
        Assert.Contains("Staff member updated.", html);
        Assert.Contains("alert-green", html);
        Assert.Contains($"href=\"/staff/{member.Id}\"", html);
    }

    [Fact]
    public async Task Edit_duplicate_email_stays_on_the_form_and_leaves_the_saved_profile_unchanged()
    {
        var member = await CreateMemberAsync();
        var existing = await MemberAsync(StaffSeedIds.StaffMembers.AnnaPetrosyan);
        var fields = EditFields(member);
        fields["Input.FirstName"] = "Keep this entry";
        fields["Input.CorporateEmail"] = existing.CorporateEmail;
        using var client = await ClientAsync();

        var response = await PostFormAsync(client, $"/staff/{member.Id}/edit", fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Corporate email is already in use.", html);
        Assert.Contains("aria-invalid=\"true\"", InputTag(html, "Input.CorporateEmail"));
        Assert.Contains("value=\"Keep this entry\"", InputTag(html, "Input.FirstName"));
        Assert.Equal(member, await MemberAsync(member.Id));
    }

    [Fact]
    public async Task A_missing_staff_member_gets_404()
    {
        using var client = await ClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/staff/{Guid.NewGuid()}/edit")).StatusCode);
    }

    [Theory]
    [InlineData("/staff/new")]
    [InlineData("/staff/import")]
    [InlineData("/staff/20000000-0000-0000-0003-000000000003/edit")]
    public async Task A_staff_member_gets_403_on_get_and_post(string path)
    {
        using var client = await UiTestClient.SignedInAsync(factory,
            LoginPage.Email(StaffSeedIds.StaffMembers.AnnaPetrosyan), StaffSeedData.Password);

        await AssertForbiddenAsync(client, await client.GetAsync(path));
        await AssertForbiddenAsync(client, await client.PostAsync(path, new FormUrlEncodedContent([])));
    }

    [Theory]
    [InlineData("/staff/new")]
    [InlineData("/staff/import")]
    [InlineData("/staff/20000000-0000-0000-0003-000000000003/edit")]
    public async Task A_signed_out_user_is_sent_to_login(string path)
    {
        using var client = UiTestClient.Anonymous(factory);
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData("/staff/new")]
    [InlineData("/staff/import")]
    [InlineData("/staff/20000000-0000-0000-0003-000000000003/edit")]
    public async Task Writes_without_an_antiforgery_token_are_rejected(string path)
    {
        using var client = await ClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, new FormUrlEncodedContent([]))).StatusCode);
    }

    [Fact]
    public async Task A_bad_csv_row_is_listed_and_no_rows_are_imported()
    {
        var valid = Request();
        var invalid = Request() with { CorporateEmail = "invalid@gmail.com" };
        using var client = await ClientAsync();

        var response = await PostCsvAsync(client, CsvHeader + CsvRow(valid) + CsvRow(invalid));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<td data-label=\"Row\">3</td>", html);
        Assert.Contains("<td data-label=\"Field\">CorporateEmail</td>", html);
        Assert.Contains("Only Ameriabank staff can be enrolled", html);
        Assert.Contains("Nothing was imported.", html);
        Assert.False(await ExistsAsync(valid.EmployeeId));
        Assert.False(await ExistsAsync(invalid.EmployeeId));
    }

    [Fact]
    public async Task Successful_csv_import_uses_prg_and_shows_passwords_once()
    {
        var request = Request();
        using var client = await ClientAsync();

        var response = await PostCsvAsync(client, CsvHeader + CsvRow(request));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ImportPage, response.Headers.Location!.OriginalString);
        Assert.True(await ExistsAsync(request.EmployeeId));
        KeepCookies(client, response);

        var receipt = await client.GetAsync(ImportPage);
        var html = await receipt.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.True(receipt.Headers.CacheControl?.NoStore == true);
        Assert.Contains("alert-green", html);
        Assert.Contains("1 staff members imported.", html);
        Assert.Contains(request.EmployeeId, html);
        Assert.Contains("data-label=\"Temporary password\"", html);
        var member = await FindAsync(request.CorporateEmail);
        Assert.Contains($"href=\"/staff/{member.Id}\"", html);

        KeepCookies(client, receipt);
        Assert.DoesNotContain(request.EmployeeId, await (await client.GetAsync(ImportPage)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_import_receipt_is_bound_to_the_user_who_uploaded_it()
    {
        var request = Request();
        using var uploader = await ClientAsync();
        var imported = await PostCsvAsync(uploader, CsvHeader + CsvRow(request));
        Assert.Equal(HttpStatusCode.Redirect, imported.StatusCode);

        // Even replaying the protected TempData reference with another signed-in account reveals no passwords.
        using var other = await ClientAsync(administrator: true);
        KeepCookies(other, imported);
        Assert.DoesNotContain(request.EmployeeId, await (await other.GetAsync(ImportPage)).Content.ReadAsStringAsync());

        KeepCookies(uploader, imported);
        Assert.Contains(request.EmployeeId, await (await uploader.GetAsync(ImportPage)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_file_over_one_mb_is_shown_as_a_file_error()
    {
        using var client = await ClientAsync();
        var response = await PostCsvAsync(client, new string('x', (int)factory.Services.GetRequiredService<IOptions<StaffOptions>>().Value.Import.MaxFileBytes + 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The file must be 1 MB or smaller.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task More_than_200_rows_are_rejected_without_importing()
    {
        var rows = Enumerable.Range(0, 201).Select(_ => Request()).ToArray();
        using var client = await ClientAsync();

        var response = await PostCsvAsync(client, CsvHeader + string.Concat(rows.Select(CsvRow)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("at most 200 data rows", await response.Content.ReadAsStringAsync());
        Assert.False(await ExistsAsync(rows[0].EmployeeId));
    }

    [Fact]
    public async Task An_empty_upload_has_a_clear_error()
    {
        using var client = await ClientAsync();
        var response = await PostCsvAsync(client, "");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose a non-empty CSV file.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_template_uses_the_backend_column_names()
    {
        using var client = await ClientAsync();
        var response = await client.GetAsync(ImportPage + "?handler=Template");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CsvHeader, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Manual_enrolment_redirects_the_employee_until_the_initial_password_is_changed()
    {
        var request = Request();
        using var librarian = await ClientAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(librarian, NewPage, Fields(request))).StatusCode);

        using var employee = await UiTestClient.SignedInAsync(factory, request.CorporateEmail, request.Password);
        var blocked = await employee.GetAsync("/catalog");
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Equal("/change-password", blocked.Headers.Location!.OriginalString);

        // The password-change page is a separate UI task; use the existing API to change it.
        using var api = factory.CreateClient();
        var login = await api.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(request.CorporateEmail, request.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var account = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.True(account.PasswordChangeRequired);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var change = await api.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest(request.Password, "ChangedPass9"));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        using var signedInAgain = await UiTestClient.SignedInAsync(factory, request.CorporateEmail, "ChangedPass9");
        Assert.Equal(HttpStatusCode.OK, (await signedInAgain.GetAsync("/catalog")).StatusCode);
        await AssertForbiddenAsync(signedInAgain, await signedInAgain.GetAsync(NewPage));
    }

    [Fact]
    public async Task A_deactivated_member_shows_a_notice_and_profile_navigation_instead_of_editable_fields()
    {
        using var client = await ClientAsync();
        var member = await MemberAsync(StaffSeedIds.StaffMembers.HaykManukyan);
        var response = await client.GetAsync($"/staff/{member.Id}/edit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("This staff member is deactivated. Their profile cannot be edited.", html);
        Assert.Contains("alert-amber", html);
        Assert.Contains($"href=\"/staff/{member.Id}\"", html);
        Assert.DoesNotContain("name=\"Input.", html);
        Assert.DoesNotContain("Save changes", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_and_stale_edit_posts_cannot_change_a_deactivated_profile_or_unlock_its_account(
        bool staleForm)
    {
        var member = await CreateMemberAsync();
        var path = $"/staff/{member.Id}/edit";
        using var client = await ClientAsync();
        var token = await AntiforgeryAsync(client, staleForm ? path : NewPage);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IStaffDeactivationService>()
                .DeactivateAsync(member.Id, new("Left the bank"));
        }

        var before = await MemberAsync(member.Id);
        var accountBefore = await AccountStateAsync(member.CorporateEmail);
        Assert.True(accountBefore.LockoutEnabled);
        Assert.Equal(DateTimeOffset.MaxValue, accountBefore.LockoutEnd);
        var fields = EditFields(member);
        fields["Input.FirstName"] = "Tampered";
        fields["Input.CorporateEmail"] = $"tampered.{Guid.NewGuid():N}@ameriabank.am";
        fields["Input.DepartmentId"] = StaffSeedIds.Departments.Finance.ToString();
        fields["Input.StaffCategoryId"] = StaffSeedIds.Categories.ManagerSenior.ToString();
        fields["Input.Role"] = StaffRoles.LibraryAdministrator;
        fields["Input.Status"] = nameof(StaffMemberStatus.Active);
        fields["__RequestVerificationToken"] = token;

        var response = await client.PostAsync(path, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("This staff member is deactivated. Their profile cannot be edited.", html);
        Assert.DoesNotContain("name=\"Input.", html);
        Assert.Equal(before, await MemberAsync(member.Id));
        Assert.Equal(accountBefore, await AccountStateAsync(member.CorporateEmail));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_forms_keep_all_common_fields_labels_lookups_values_and_validation(bool edit)
    {
        var member = await CreateMemberAsync();
        var path = edit ? $"/staff/{member.Id}/edit" : NewPage;
        var fields = edit ? EditFields(member) : Fields(Request());
        fields["Input.FirstName"] = "Preserved name";
        fields["Input.LastName"] = "";
        fields["Input.CorporateEmail"] = "invalid-email";
        fields["Input.Position"] = "Senior engineer";
        fields["Input.LineManagerEmail"] = "invalid-manager";
        fields["Input.DepartmentId"] = StaffSeedIds.Departments.Finance.ToString();
        fields["Input.StaffCategoryId"] = StaffSeedIds.Categories.ManagerSenior.ToString();
        using var client = await ClientAsync();

        var response = await PostFormAsync(client, path, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        foreach (var field in new[]
                 {
                     "FirstName", "LastName", "CorporateEmail", "DepartmentId", "Position", "LineManagerEmail",
                     "StaffCategoryId"
                 })
        {
            var control = Regex.Match(html, "<(?:input|select)\\b[^>]*\\bname=\"Input\\." + field + "\"[^>]*>");
            Assert.True(control.Success, $"Input.{field} was not rendered.");
            Assert.Single(Regex.Matches(html, "<(?:input|select)\\b[^>]*\\bname=\"Input\\." + field + "\"[^>]*>"));
            Assert.Contains($"<label for=\"Input_{field}\">", html);
            Assert.Contains($"data-valmsg-for=\"Input.{field}\"", html);
            var errorId = Regex.Match(control.Value, "aria-describedby=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(errorId);
            Assert.Contains($"id=\"{errorId}\"", html);
        }

        foreach (var field in new[] { "FirstName", "LastName", "CorporateEmail", "Position", "LineManagerEmail" })
            Assert.Contains($"value=\"{fields[$"Input.{field}"]}\"", InputTag(html, $"Input.{field}"));
        foreach (var field in new[] { "LastName", "CorporateEmail", "LineManagerEmail" })
            Assert.Contains("aria-invalid=\"true\"", InputTag(html, $"Input.{field}"));
        foreach (var field in new[] { "DepartmentId", "StaffCategoryId" })
            Assert.Matches(
                "<option\\b(?=[^>]*selected=\"selected\")(?=[^>]*value=\"" + fields[$"Input.{field}"] + "\")[^>]*>",
                html);
        Assert.Contains("form form-2", html);
        Assert.Equal(member, await MemberAsync(member.Id));
    }

    private static async Task AssertForbiddenAsync(
        HttpClient client,
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var target = new Uri(client.BaseAddress!, response.Headers.Location!);
        Assert.Equal("/forbidden", target.AbsolutePath);

        using var forbidden = await client.GetAsync(target.PathAndQuery);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private Task<HttpClient> ClientAsync(bool administrator = false) => UiTestClient.SignedInAsync(factory,
        LoginPage.Email(administrator ? StaffSeedIds.StaffMembers.LilitGrigoryan : StaffSeedIds.StaffMembers.AramSargsyan),
        StaffSeedData.Password);

    private static EnrolStaffMemberRequest Request() => new(
        $"UI-{Guid.NewGuid():N}", "Demo", "Employee", $"ui.{Guid.NewGuid():N}@ameriabank.am",
        StaffSeedIds.Departments.InformationTechnology, "Engineer", null, StaffSeedIds.Categories.Employee, "DemoPass1");

    private static Dictionary<string, string> Fields(EnrolStaffMemberRequest request) => new()
    {
        ["Input.EmployeeId"] = request.EmployeeId,
        ["Input.FirstName"] = request.FirstName,
        ["Input.LastName"] = request.LastName,
        ["Input.CorporateEmail"] = request.CorporateEmail,
        ["Input.DepartmentId"] = request.DepartmentId.ToString(),
        ["Input.Position"] = request.Position,
        ["Input.LineManagerEmail"] = request.LineManagerEmail ?? "",
        ["Input.StaffCategoryId"] = request.StaffCategoryId.ToString(),
        ["Input.Password"] = request.Password,
        ["Input.Role"] = request.Role ?? StaffRoles.StaffMember,
    };

    private static Dictionary<string, string> EditFields(StaffMemberDto member) => new()
    {
        ["Input.FirstName"] = member.FirstName,
        ["Input.LastName"] = member.LastName,
        ["Input.CorporateEmail"] = member.CorporateEmail,
        ["Input.DepartmentId"] = member.DepartmentId.ToString(),
        ["Input.Position"] = member.Position,
        ["Input.LineManagerEmail"] = member.LineManagerEmail ?? "",
        ["Input.StaffCategoryId"] = member.StaffCategoryId.ToString(),
    };

    private static string CsvRow(EnrolStaffMemberRequest request) =>
        $"{request.EmployeeId},{request.FirstName},{request.LastName},{request.CorporateEmail},Information Technology,{request.Position},,Employee,{request.Role ?? StaffRoles.StaffMember}\r\n";

    private async Task<StaffMemberDto> CreateMemberAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStaffManagementService>().EnrolAsync(Request());
    }

    private async Task<StaffMemberDto> MemberAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStaffManagementService>().GetAsync(id);
    }

    private async Task<StaffMemberDto> FindAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var id = await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().StaffMembers.AsNoTracking()
            .Where(member => member.CorporateEmail == email).Select(member => member.Id).SingleAsync();
        return await scope.ServiceProvider.GetRequiredService<IStaffManagementService>().GetAsync(id);
    }

    private async Task<bool> ExistsAsync(string employeeId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().StaffMembers.AsNoTracking()
            .AnyAsync(member => member.EmployeeId == employeeId);
    }

    private async Task<(string? Email, bool LockoutEnabled, DateTimeOffset? LockoutEnd, string? SecurityStamp)>
        AccountStateAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Users.AsNoTracking()
            .SingleAsync(user => user.Email == email);
        return (user.Email, user.LockoutEnabled, user.LockoutEnd, user.SecurityStamp);
    }

    private static async Task<string> AntiforgeryAsync(HttpClient client, string path)
    {
        using var page = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        KeepCookies(client, page);
        var token = Regex.Match(await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return WebUtility.HtmlDecode(token);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await AntiforgeryAsync(client, path);
        using var body = new FormUrlEncodedContent(fields);
        return await client.PostAsync(path, body);
    }

    private static async Task<HttpResponseMessage> PostCsvAsync(HttpClient client, string csv)
    {
        // The upload starts on Enrol, matching the sidebar form used by the browser.
        var token = await AntiforgeryAsync(client, NewPage);
        using var body = new MultipartFormDataContent();
        body.Add(new StringContent(token), "__RequestVerificationToken");
        body.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "CsvFile", "staff.csv");
        return await client.PostAsync(ImportPage, body);
    }

    private static string InputTag(string html, string field)
    {
        var match = Regex.Match(html, "<input\\b[^>]*\\bname=\"" + Regex.Escape(field) + "\"[^>]*>");
        Assert.True(match.Success, $"Input '{field}' was not rendered.");
        return match.Value;
    }

    private static void KeepCookies(HttpClient client, HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        var existing = client.DefaultRequestHeaders.TryGetValues("Cookie", out var headers) ? string.Join("; ", headers) : "";
        foreach (var pair in (existing + "; " + UiTestClient.SetCookies(response)).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator >= 0)
                cookies[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
        }
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(pair => $"{pair.Key}={pair.Value}")));
    }
}
