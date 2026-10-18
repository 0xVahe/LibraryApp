using LibraryApp.Application.Catalog.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.IntegrationTests.Ui;

/// <summary>The shell's host with covers turned off (<c>Catalog:Covers:BaseUrl</c> empty).</summary>
public sealed class NoCoversHostFactory : ApiHostFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Catalog:Covers:BaseUrl", "");
    }
}

/// <summary>
/// Images on the pages may come only from this site and from the configured cover storage — the account's path, never
/// the whole storage host, so pictures of other accounts on the same service cannot be shown (IO-28).
/// </summary>
public class UiCoverCspTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    internal static async Task<string> ImageSourcesAsync(ApiHostFactory host)
    {
        var page = await UiTestClient.Anonymous(host).GetAsync("/notfound");
        string csp = page.Headers.GetValues("Content-Security-Policy").Single();
        return csp.Split(';').Select(part => part.Trim()).Single(part => part.StartsWith("img-src ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Images_may_come_from_the_cover_path_of_the_account_but_not_from_the_whole_host()
    {
        string baseUrl = factory.Services.GetRequiredService<IOptions<CatalogCoversOptions>>().Value.BaseUrl;
        var uri = new Uri(baseUrl);
        Assert.NotEqual("/", uri.AbsolutePath);   // the setting names an account path, not a bare host

        string sources = await ImageSourcesAsync(factory);

        Assert.Equal($"img-src 'self' data: {baseUrl}", sources);
        Assert.DoesNotContain($"{uri.Scheme}://{uri.Host} ", sources + " ");
        Assert.DoesNotContain($"{uri.Scheme}://{uri.Host}/ ", sources + " ");
    }
}

/// <summary>An empty <c>Catalog:Covers:BaseUrl</c> turns covers off: the pages accept images from this site only.</summary>
public class UiCoverCspDisabledTests(NoCoversHostFactory factory) : IClassFixture<NoCoversHostFactory>
{
    [Fact]
    public async Task Without_a_cover_storage_images_come_only_from_this_site() =>
        Assert.Equal("img-src 'self' data:", await UiCoverCspTests.ImageSourcesAsync(factory));
}
