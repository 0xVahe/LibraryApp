using LibraryApp.Application.Catalog;

namespace LibraryApp.Application.Tests.Catalog;

public class CatalogLanguagesTests
{
    [Fact]
    public void The_collection_has_Armenian_English_and_Russian_by_name()
    {
        Assert.Equal(new[] { "hy", "en", "ru" }, CatalogLanguages.All.Select(x => x.Code));
        Assert.Equal(new[] { "Armenian", "English", "Russian" }, CatalogLanguages.All.Select(x => x.Name));
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("EN", "English")]
    [InlineData(" hy ", "Armenian")]
    [InlineData("ru", "Russian")]
    public void A_known_code_shows_its_name(string code, string expected) =>
        Assert.Equal(expected, CatalogLanguages.NameOf(code));

    [Fact]
    public void An_unknown_code_is_shown_as_it_is() =>
        Assert.Equal("de", CatalogLanguages.NameOf("de"));

    [Theory]
    [InlineData("en", true)]
    [InlineData("Ru", true)]
    [InlineData("de", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_three_codes_are_known(string? code, bool expected) =>
        Assert.Equal(expected, CatalogLanguages.IsKnown(code));
}
