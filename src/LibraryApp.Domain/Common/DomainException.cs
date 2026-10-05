namespace LibraryApp.Domain.Common;

public sealed class DomainException(
        Error error, Exception?
        innerException = null) : Exception(error.Message, innerException)
{
    public Error Error { get; } = error;
}