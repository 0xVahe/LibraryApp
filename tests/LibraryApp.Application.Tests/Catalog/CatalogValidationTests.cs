using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Domain.Catalog;
using LibraryApp.Application.Catalog.Options;
using LibraryApp.Application.Tests.Fakes;
using Microsoft.Extensions.Options;

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

    [Fact]
    public void Create_book_requires_at_least_one_author()
    {
        var validator = new CreateBookRequestValidator(
            new TestClock(new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc)));

        var request = new CreateBookRequest(
            "Test book",
            "9780306406157",
            2024,
            "en",
            null,
            Guid.NewGuid(),
            null,
            null!);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.PropertyName == nameof(CreateBookRequest.AuthorIds));
    }

    [Theory]
    [InlineData("4006381333931")]
    [InlineData("5901234123457")]
    [InlineData("0000000000000")]
    public void Create_book_rejects_thirteen_digit_values_without_isbn_prefix(
        string isbn)
    {
        var validator = new CreateBookRequestValidator(new TestClock(new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc)));

        var request = new CreateBookRequest(
            "Test book",
            isbn,
            2024,
            "en",
            null,
            Guid.NewGuid(),
            null,
            [Guid.NewGuid()]);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.PropertyName == nameof(CreateBookRequest.Isbn));
    }

    [Fact]
    public void Create_book_rejects_a_cover_path_when_cover_storage_is_disabled()
    {
        var validator = new CreateBookRequestValidator(
            new TestClock(new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc)),
            Options.Create(new CatalogCoversOptions()));

        var result = validator.Validate(new CreateBookRequest(
            "Test book",
            "9780306406157",
            2024,
            "en",
            null,
            Guid.NewGuid(),
            null,
            [Guid.NewGuid()],
            "library-covers/01.png"));

        var error = Assert.Single(result.Errors, error =>
            error.PropertyName == nameof(CreateBookRequest.CoverImagePath));
        Assert.Equal(CoverImagePathRules.Message, error.ErrorMessage);
    }

    [Fact]
    public void Update_book_checks_only_the_cover_path_format_so_a_saved_cover_does_not_block_an_edit()
    {
        // The update validator does not depend on Catalog:Covers: with covers off the Edit form still sends the saved path.
        var validator = new UpdateBookRequestValidator(new TestClock(new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc)));

        UpdateBookRequest Request(string coverImagePath) =>
            new("Test book", "9780306406157", 2024, "en", null, Guid.NewGuid(), null, coverImagePath);

        Assert.True(validator.Validate(Request("library-covers/01.png")).IsValid);
        var error = Assert.Single(validator.Validate(Request("/library-covers/01.png")).Errors);
        Assert.Equal(nameof(UpdateBookRequest.CoverImagePath), error.PropertyName);
        Assert.Equal(CoverImagePathRules.Message, error.ErrorMessage);
    }
}
