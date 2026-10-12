using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Tests.Common;

public class ErrorTests
{
    private static readonly Error RenewMax = Error.BusinessRule("borrowing.renew_max", "Maximum renewals reached");

    [Fact]
    public void Two_errors_with_the_same_code_message_and_kind_are_equal()
    {
        var same = Error.BusinessRule("borrowing.renew_max", "Maximum renewals reached");

        Assert.Equal(RenewMax, same);
    }

    [Theory]
    [InlineData(ErrorKind.Validation)]
    [InlineData(ErrorKind.Forbidden)]
    [InlineData(ErrorKind.NotFound)]
    [InlineData(ErrorKind.Conflict)]
    [InlineData(ErrorKind.Unauthorized)]
    public void Factory_methods_set_the_kind(ErrorKind kind)
    {
        var error = kind switch
        {
            ErrorKind.Validation => Error.Validation("x.y", "m"),
            ErrorKind.Forbidden => Error.Forbidden("x.y", "m"),
            ErrorKind.NotFound => Error.NotFound("x.y", "m"),
            ErrorKind.Unauthorized => Error.Unauthorized("x.y", "m"),
            _ => Error.Conflict("x.y", "m"),
        };

        Assert.Equal(kind, error.Kind);
    }

    [Fact]
    public void DomainException_carries_the_error_and_uses_its_message()
    {
        var exception = new DomainException(RenewMax);

        Assert.Equal(RenewMax, exception.Error);
        Assert.Equal("Maximum renewals reached", exception.Message);
    }
}
