using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Validators;

namespace LibraryApp.Application.Tests.Catalog;

public class PublisherRequestValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Missing_or_blank_name_is_rejected(string? name)
    {
        var result = new PublisherRequestValidator().Validate(new PublisherRequest(name));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(PublisherRequest.Name));
    }

    [Theory]
    [InlineData("Demo Publisher")]
    [InlineData("  Demo Publisher  ")]
    public void Valid_name_is_accepted(string name) =>
        Assert.True(new PublisherRequestValidator().Validate(new PublisherRequest(name)).IsValid);

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void Name_length_limit_is_200_characters(int length, bool expected) =>
        Assert.Equal(expected, new PublisherRequestValidator().Validate(new PublisherRequest(new string('a', length))).IsValid);
}
