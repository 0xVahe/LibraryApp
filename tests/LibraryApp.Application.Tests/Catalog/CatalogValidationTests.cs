using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Tests.Catalog;

public class CatalogValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Names_are_required(string? name)
    {
        Assert.False(new AuthorRequestValidator().Validate(new AuthorRequest(name)).IsValid);
        Assert.False(new CategoryRequestValidator().Validate(new CategoryRequest(name, null)).IsValid);
    }

    [Fact]
    public void Name_limits_match_existing_mappings()
    {
        var authors = new AuthorRequestValidator();
        Assert.True(authors.Validate(new AuthorRequest(new string('a', Author.FullNameMaxLength))).IsValid);
        Assert.False(authors.Validate(new AuthorRequest(new string('a', Author.FullNameMaxLength + 1))).IsValid);
        var categories = new CategoryRequestValidator();
        Assert.True(categories.Validate(new CategoryRequest(new string('a', Category.NameMaxLength), null)).IsValid);
        Assert.False(categories.Validate(new CategoryRequest(new string('a', Category.NameMaxLength + 1), null)).IsValid);
    }

    [Fact]
    public void Author_ids_must_be_present_and_nonempty()
    {
        var validator = new BookAuthorsRequestValidator();
        Assert.False(validator.Validate(new BookAuthorsRequest(null)).IsValid);
        Assert.False(validator.Validate(new BookAuthorsRequest([])).IsValid);
        Assert.False(validator.Validate(new BookAuthorsRequest([Guid.Empty])).IsValid);
        Assert.True(validator.Validate(new BookAuthorsRequest([Guid.NewGuid()])).IsValid);
    }

    [Fact]
    public void Optional_parent_must_be_a_nonempty_id_when_provided()
    {
        var validator = new CategoryRequestValidator();
        Assert.True(validator.Validate(new CategoryRequest("Root", null)).IsValid);
        Assert.False(validator.Validate(new CategoryRequest("Child", Guid.Empty)).IsValid);
    }
}
