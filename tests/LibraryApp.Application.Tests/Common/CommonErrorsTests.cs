using LibraryApp.Application.Common;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Common;

public class CommonErrorsTests
{
    [Fact]
    public void Platform_errors_use_codes_without_a_module_prefix()
    {
        Assert.Equal("concurrency_conflict", CommonErrors.ConcurrencyConflict.Code);
        Assert.Equal(ErrorKind.Conflict, CommonErrors.UniqueViolation.Kind);
    }
}
