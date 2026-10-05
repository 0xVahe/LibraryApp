using LibraryApp.Infrastructure.Identity;

namespace LibraryApp.Infrastructure.IntegrationTests.Identity;

public class LetterPasswordValidatorTests
{
    private readonly LetterPasswordValidator _validator = new();

    [Theory]
    [InlineData("password1")]
    [InlineData("1234567a")]
    public async Task Password_with_a_letter_is_valid(string password)
    {
        var result = await _validator.ValidateAsync(null!, new AppUser(), password);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Password_without_a_letter_is_refused(string? password)
    {
        var result = await _validator.ValidateAsync(null!, new AppUser(), password);

        Assert.False(result.Succeeded);
        Assert.Equal(LetterPasswordValidator.MissingLetter.Code, Assert.Single(result.Errors).Code);
    }
}