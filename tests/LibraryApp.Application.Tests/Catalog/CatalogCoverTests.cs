using LibraryApp.Application.Catalog.Options;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Catalog.Validators;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Tests.Catalog;

public sealed class CatalogCoverTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" ", false)]
    [InlineData("library-covers/01.png", true)]
    [InlineData("covers/book_01.webp", true)]
    [InlineData("https://images.example.test/01.png", false)]
    [InlineData("/library-covers/01.png", false)]
    [InlineData("library-covers/../01.png", false)]
    [InlineData("library-covers\\01.png", false)]
    [InlineData("library-covers/01.gif", false)]
    [InlineData("library-covers/01.png?raw=1", false)]
    public void Cover_paths_follow_the_storage_independent_format(string? path, bool expected) =>
        Assert.Equal(expected, CoverImagePathRules.IsValid(path));

    [Fact]
    public void Cover_paths_over_300_characters_are_rejected() =>
        Assert.False(CoverImagePathRules.IsValid($"covers/{new string('a', 294)}.png"));

    [Fact]
    public void A_nonempty_cover_path_is_rejected_when_cover_storage_is_disabled() =>
        Assert.False(CoverImagePathRules.IsValid("library-covers/01.png", coversEnabled: false));

    [Fact]
    public void Cover_url_builder_uses_the_configured_base_url()
    {
        var builder = new CatalogCoverUrlBuilder(Options.Create(new CatalogCoversOptions
        {
            BaseUrl = "https://images.example.test/image/upload/",
        }));

        Assert.Equal("https://images.example.test/image/upload/library-covers/01.png",
            builder.Build("library-covers/01.png"));
        Assert.Null(builder.Build(null));
    }

    [Fact]
    public void Changing_the_configured_base_url_changes_the_returned_url()
    {
        var first = new CatalogCoverUrlBuilder(Options.Create(new CatalogCoversOptions
        {
            BaseUrl = "https://first.example.test/",
        }));
        var second = new CatalogCoverUrlBuilder(Options.Create(new CatalogCoversOptions
        {
            BaseUrl = "https://second.example.test/",
        }));

        Assert.Equal("https://first.example.test/library-covers/01.png", first.Build("library-covers/01.png"));
        Assert.Equal("https://second.example.test/library-covers/01.png", second.Build("library-covers/01.png"));
    }

    [Fact]
    public void An_empty_base_url_turns_cover_urls_off()
    {
        var builder = new CatalogCoverUrlBuilder(Options.Create(new CatalogCoversOptions()));

        Assert.Null(builder.Build("library-covers/01.png"));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", false)]
    [InlineData("http://images.example.test/", false)]
    [InlineData("images.example.test/", false)]
    [InlineData("https://images.example.test", false)]
    [InlineData("https://user@images.example.test/", false)]
    [InlineData("https://images.example.test/?raw=1", false)]
    [InlineData("https://images.example.test/#covers", false)]
    [InlineData("https://images.example.test/path with spaces/", false)]
    [InlineData("https://images.example.test/path;bad/", false)]
    [InlineData("https://images.example.test/path'bad/", false)]
    [InlineData("https://images.example.test/path,bad/", false)]
    [InlineData("https://images.example.test/", true)]
    public void Cover_base_url_is_validated_at_start(string baseUrl, bool expected)
    {
        var result = new CatalogCoversOptionsValidator().Validate(null, new CatalogCoversOptions
        {
            BaseUrl = baseUrl,
        });

        Assert.Equal(expected, result.Succeeded);
    }
}
