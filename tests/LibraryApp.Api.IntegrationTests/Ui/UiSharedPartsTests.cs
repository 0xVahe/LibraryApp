using System.Security.Claims;
using FluentValidation;
using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LibraryApp.Api.IntegrationTests.Ui;

/// <summary>The shared pieces of the UI shell without a host: navigation, badges, pager, form validation, domain errors.</summary>
public class UiSharedPartsTests
{
    private static ClaimsPrincipal User(string email, params string[] roles)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Email, email) };
        claims.AddRange(roles.Select(role => new Claim(LibraryClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", JwtRegisteredClaimNames.Email, LibraryClaimTypes.Role));
    }

    [Fact]
    public void Navigation_follows_the_role()
    {
        static string[] Texts(ClaimsPrincipal user) => UiNavigation.For(user).Select(i => i.Text).ToArray();

        Assert.Equal(["My library", "Catalog", "Notifications"], Texts(User("anna.petrosyan@ameriabank.am", StaffRoles.StaffMember)));
        Assert.Equal(["Loan desk", "Staff members", "Catalog", "My library", "Notifications"],
            Texts(User("aram.sargsyan@ameriabank.am", StaffRoles.Librarian)));
        Assert.Equal(["Loan desk", "Staff members", "Catalog", "My library", "Notifications", "Settings"],
            Texts(User("lilit.grigoryan@ameriabank.am", StaffRoles.LibraryAdministrator)));
        Assert.Empty(UiNavigation.For(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void A_pending_first_login_password_change_hides_the_navigation()
    {
        var user = User("new.employee@ameriabank.am");
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(LibraryClaimTypes.PasswordChangeRequired, bool.TrueString));

        Assert.Empty(UiNavigation.For(user));
    }

    [Fact]
    public void The_current_item_also_covers_the_pages_below_it()
    {
        var catalog = new NavItem("Catalog", UiNavigation.Catalog);

        Assert.True(UiNavigation.IsCurrent(catalog, "/catalog/books/42"));
        Assert.False(UiNavigation.IsCurrent(catalog, "/catalogue"));
    }

    [Fact]
    public void The_user_chip_shows_initials_and_role()
    {
        var user = User("anna.petrosyan@ameriabank.am", StaffRoles.Librarian);

        Assert.Equal("AP", UiNavigation.Initials(user));
        Assert.Equal("Anna Petrosyan", UiNavigation.DisplayName(user));

        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(JwtRegisteredClaimNames.Name, "Anna Petrosyan-Hakobyan"));
        Assert.Equal("Anna Petrosyan-Hakobyan", UiNavigation.DisplayName(user));
        Assert.Equal("Librarian", UiNavigation.RoleName(user));
    }

    public static TheoryData<Enum> Statuses()
    {
        var data = new TheoryData<Enum>();
        foreach (var type in new[] { typeof(StaffMemberStatus), typeof(LoanStatus), typeof(BookCopyStatus), typeof(ReservationStatus) })
            foreach (Enum value in Enum.GetValues(type))
                data.Add(value);
        return data;
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public void Every_status_has_a_badge_colour_of_the_design(Enum status)
    {
        var badge = StatusBadge.For(status);

        Assert.Contains(badge.CssClass, new[] { "badge badge-green", "badge badge-amber", "badge badge-red", "badge badge-blue", "badge badge-grey" });
        Assert.False(string.IsNullOrWhiteSpace(badge.Text));
    }

    [Fact]
    public void Badge_labels_are_words()
    {
        Assert.Equal("Ready for pickup", StatusBadge.Label(ReservationStatus.ReadyForPickup));
        Assert.Equal("On loan", StatusBadge.Label(BookCopyStatus.OnLoan));
        Assert.Equal("badge badge-red", StatusBadge.For(LoanStatus.Overdue).CssClass);
    }

    [Fact]
    public void Pager_links_keep_the_filters_and_mark_the_current_page()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/staff";
        context.Request.QueryString = new QueryString("?search=anna&page=2&status=Active");

        var pager = PagerModel.From(new PagedResult<int>([], TotalCount: 45, Page: 2, PageSize: 20), context.Request);

        Assert.Equal([1, 2, 3], pager.Links.Select(l => l.Page));
        Assert.Equal("/staff?search=anna&status=Active&page=3", pager.Links[2].Href);
        Assert.True(pager.Links[1].Current);
    }

    [Fact]
    public void One_page_needs_no_pager()
    {
        var pager = PagerModel.From(new PagedResult<int>([1], 1, 1, 20), new DefaultHttpContext().Request);

        Assert.Empty(pager.Links);
    }

    public sealed record SampleInput(string? Email, int Copies);

    private sealed class SampleInputValidator : AbstractValidator<SampleInput>
    {
        public SampleInputValidator()
        {
            RuleFor(x => x.Email).NotEmpty();
            RuleFor(x => x.Copies).InclusiveBetween(1, 4);
        }
    }

    [Fact]
    public async Task Form_validation_puts_the_errors_under_the_field_names_of_the_form()
    {
        using var services = new ServiceCollection().AddScoped<IValidator<SampleInput>, SampleInputValidator>().BuildServiceProvider();
        var modelState = new ModelStateDictionary();

        await UiValidationFilter.ValidateAsync(services, new SampleInput("", 9), "Input", modelState, CancellationToken.None);

        Assert.False(modelState.IsValid);
        Assert.Equal(["Input.Copies", "Input.Email"], modelState.Keys.Order());
    }

    [Fact]
    public async Task A_value_without_a_validator_is_valid()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var modelState = new ModelStateDictionary();

        await UiValidationFilter.ValidateAsync(services, new SampleInput("", 9), "Input", modelState, CancellationToken.None);

        Assert.True(modelState.IsValid);
    }

    private sealed class SamplePage : PageModel;

    [Theory]
    [InlineData(ErrorKind.Validation)]
    [InlineData(ErrorKind.Conflict)]
    [InlineData(ErrorKind.BusinessRule)]
    public void A_form_error_stays_on_the_page_with_its_message(ErrorKind kind)
    {
        var page = new SamplePage();

        var result = page.DomainError(new DomainException(new Error("test.code", "Copy is already on loan", kind)));

        Assert.IsType<PageResult>(result);
        Assert.Equal("Copy is already on loan", page.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
    }

    [Theory]
    [InlineData(ErrorKind.NotFound, typeof(NotFoundResult))]
    [InlineData(ErrorKind.Forbidden, typeof(ForbidResult))]
    [InlineData(ErrorKind.Unauthorized, typeof(ChallengeResult))]
    public void Other_kinds_get_their_page(ErrorKind kind, Type expected)
    {
        var result = new SamplePage().DomainError(new DomainException(new Error("test.code", "Message", kind)));

        Assert.IsType(expected, result);
    }

    [Fact]
    public void An_unknown_kind_is_not_hidden()
    {
        var exception = new DomainException(new Error("test.code", "Message", (ErrorKind)999));

        Assert.Same(exception, Assert.Throws<DomainException>(() => new SamplePage().DomainError(exception)));
    }

    [Theory]
    [InlineData(404, "Page not found")]
    [InlineData(403, "You don’t have access to this page")]
    [InlineData(500, "Something went wrong")]
    public void Status_messages_by_code(int code, string title)
    {
        var message = StatusMessageModel.For(code, "trace-1");

        Assert.Equal(title, message.Title);
        Assert.Equal(code >= 500 ? "trace-1" : null, message.TraceId);
    }

    [Theory]
    [InlineData("00:05:00", true)]
    [InlineData("00:00:00", true)]
    [InlineData("-00:00:01", false)]
    public void The_security_stamp_interval_must_not_be_negative(string interval, bool valid)
    {
        var result = new UiOptionsValidator().Validate(null, new UiOptions { SecurityStampValidationInterval = TimeSpan.Parse(interval) });

        Assert.Equal(valid, result.Succeeded);
    }

    public sealed class FormPage : PageModel
    {
        [BindProperty]
        public SampleInput Input { get; set; } = new("", 9);
    }

    private static PageHandlerExecutingContext PageContextFor(FormPage page, string method, IServiceProvider services,
        Dictionary<string, object?>? arguments = null)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = method;
        var property = typeof(FormPage).GetProperty(nameof(FormPage.Input))!;
        var descriptor = new CompiledPageActionDescriptor
        {
            BoundProperties =
            [
                new PageBoundPropertyDescriptor
                {
                    Name = property.Name,
                    Property = property,
                    ParameterType = property.PropertyType,
                    BindingInfo = BindingInfo.GetBindingInfo(property.GetCustomAttributes(inherit: true)),
                },
            ],
        };
        var context = new PageContext(new ActionContext(http, new RouteData(), descriptor)) { ActionDescriptor = descriptor };
        return new PageHandlerExecutingContext(context, [], null, arguments ?? [], page);
    }

    private static Task<PageHandlerExecutedContext> Next(PageHandlerExecutingContext context, Exception? exception = null) =>
        Task.FromResult(new PageHandlerExecutedContext(
            new PageContext(context) { ActionDescriptor = context.ActionDescriptor }, [], null, context.HandlerInstance) { Exception = exception });

    [Theory]
    [InlineData("POST", false)]
    [InlineData("GET", true)]   // the form is shown empty first: no errors before the user submits
    public async Task The_page_filter_validates_bound_input_only_when_it_was_posted(string method, bool expectedValid)
    {
        using var services = new ServiceCollection().AddScoped<IValidator<SampleInput>, SampleInputValidator>().BuildServiceProvider();
        var context = PageContextFor(new FormPage(), method, services);

        await new UiValidationFilter(services).OnPageHandlerExecutionAsync(context, () => Next(context));

        Assert.Equal(expectedValid, context.ModelState.IsValid);
        if (!expectedValid)
            Assert.Contains("Input.Email", context.ModelState.Keys);
    }

    [Fact]
    public async Task The_page_filter_validates_handler_arguments_too()
    {
        using var services = new ServiceCollection().AddScoped<IValidator<SampleInput>, SampleInputValidator>().BuildServiceProvider();
        var context = PageContextFor(new FormPage(), "GET", services, new() { ["request"] = new SampleInput(null, 0) });

        await new UiValidationFilter(services).OnPageHandlerExecutionAsync(context, () => Next(context));

        Assert.Equal(["Copies", "Email"], context.ModelState.Keys.Order());
    }

    [Theory]
    [InlineData(ErrorKind.NotFound, typeof(NotFoundResult))]
    [InlineData(ErrorKind.Forbidden, typeof(ForbidResult))]
    [InlineData(ErrorKind.Unauthorized, typeof(ChallengeResult))]
    public async Task An_uncaught_domain_error_on_a_page_gets_its_page(ErrorKind kind, Type expected)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = PageContextFor(new FormPage(), "GET", services);
        PageHandlerExecutedContext? executed = null;

        await new UiDomainExceptionFilter().OnPageHandlerExecutionAsync(context, async () =>
            executed = await Next(context, new DomainException(new Error("test.code", "Message", kind))));

        Assert.True(executed!.ExceptionHandled);
        Assert.IsType(expected, executed.Result);
    }

    [Fact]
    public async Task An_uncaught_form_error_is_not_hidden()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = PageContextFor(new FormPage(), "POST", services);
        PageHandlerExecutedContext? executed = null;

        await new UiDomainExceptionFilter().OnPageHandlerExecutionAsync(context, async () =>
            executed = await Next(context, new DomainException(new Error("test.code", "Message", ErrorKind.Conflict))));

        Assert.False(executed!.ExceptionHandled);   // goes on to the Error page; forms catch it with this.DomainError(ex)
    }

    private sealed class Environment(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "LibraryApp.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Theory]
    [InlineData("Production", Microsoft.AspNetCore.Http.CookieSecurePolicy.Always)]
    [InlineData("Staging", Microsoft.AspNetCore.Http.CookieSecurePolicy.Always)]
    [InlineData("Development", Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest)]
    public void Cookies_are_https_only_outside_Development(string environment, Microsoft.AspNetCore.Http.CookieSecurePolicy expected)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddLogging();
        services.Configure<JwtOptions>(o => o.AccessTokenMinutes = 60);
        services.AddUi(configuration, new Environment(environment));
        using var provider = services.BuildServiceProvider();

        var cookie = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
            .Get(LibraryApp.Api.Ui.Auth.UiAuthentication.CookieScheme);
        var antiforgery = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>>().Value;

        Assert.Equal(expected, cookie.Cookie.SecurePolicy);
        Assert.Equal(expected, antiforgery.Cookie.SecurePolicy);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.False(cookie.SlidingExpiration);
        Assert.Equal(TimeSpan.FromMinutes(60), cookie.ExpireTimeSpan);
    }

    [Theory]
    [InlineData("/api/loans", true)]
    [InlineData("/API/loans", true)]
    [InlineData("/api", true)]
    [InlineData("/apis", false)]
    [InlineData("/catalog/api", false)]
    public void The_api_is_exactly_the_api_segment(string path, bool api) =>
        Assert.Equal(api, UiPaths.IsApi(new PathString(path)));
}

