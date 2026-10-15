using System.Reflection;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Common;

/// <summary>
/// The error conventions of every module (SD-04, SD-05), checked over all <c>*Errors</c> classes of Domain and
/// Application: a new error that breaks them fails here, in the pull request that adds it.
/// </summary>
public class ErrorCatalogTests
{
    private static readonly Assembly[] Assemblies = [typeof(Error).Assembly, typeof(CommonErrors).Assembly];

    /// <summary>Every static <see cref="Error"/> field of a class named <c>…Errors</c>, with that class.</summary>
    public static IEnumerable<object[]> Errors() =>
        Assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name.EndsWith("Errors", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(Error))
                .Select(field => new object[] { type, field.Name, (Error)field.GetValue(null)! }));

    [Theory]
    [MemberData(nameof(Errors))]
    public void A_module_error_code_starts_with_its_module(Type owner, string name, Error error)
    {
        if (owner == typeof(CommonErrors))
        {
            // Platform errors: no module prefix.
            Assert.Matches("^[a-z][a-z0-9_]*$", error.Code);
            return;
        }

        // LibraryApp.Domain.<Module> → <module>.<what_happened> in lower snake case.
        var module = owner.Namespace!.Split('.').Last().ToLowerInvariant();
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(error.Code, $"^{module}\\.[a-z][a-z0-9_]*$"),
            $"{owner.Name}.{name} has code '{error.Code}'; expected '{module}.<what_happened>' in lower snake case.");
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public void An_error_has_a_message_for_the_client(Type owner, string name, Error error)
    {
        Assert.False(string.IsNullOrWhiteSpace(error.Message), $"{owner.Name}.{name} has no message.");
        Assert.True(Enum.IsDefined(error.Kind), $"{owner.Name}.{name} has an unknown kind.");
    }

    [Fact]
    public void Error_codes_are_unique_across_the_system()
    {
        var duplicates = Errors()
            .Select(row => (Owner: ((Type)row[0]).Name, Name: (string)row[1], ((Error)row[2]).Code))
            .GroupBy(row => row.Code)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(row => $"{row.Owner}.{row.Name}"))}")
            .ToList();

        Assert.True(duplicates.Count == 0, "The same code is defined more than once: " + string.Join("; ", duplicates));
    }
}
