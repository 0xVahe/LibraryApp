using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;

namespace LibraryApp.Domain.Tests.Events;

/// <summary>
/// Rules for every event record (SD-08, INT-07): it lives in Domain/Events, is a sealed record, carries only ids and
/// simple values (never entities, names or e-mails), and its time fields are UTC by name. Runs over all events,
/// so a new event is checked automatically.
/// </summary>
public class DomainEventRulesTests
{
    private static readonly Type[] AllowedTypes =
    [
        typeof(Guid), typeof(Guid?), typeof(DateTime), typeof(DateTime?), typeof(int), typeof(int?),
        typeof(bool), typeof(string), typeof(decimal),
    ];

    private static IReadOnlyList<Type> EventTypes() =>
        typeof(IDomainEvent).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
            .ToList();

    public static TheoryData<Type> Events()
    {
        var data = new TheoryData<Type>();
        foreach (var type in EventTypes())
            data.Add(type);
        return data;
    }

    [Fact]
    public void All_events_of_the_requirements_exist()
    {
        string[] expected =
        [
            nameof(LoanIssued), nameof(LoanReturned), nameof(LoanRenewed), nameof(LoanOverdue),
            nameof(ReservationReady), nameof(PenaltyApplied), nameof(StaffStatusChanged), nameof(StaffEnrolled),
        ];

        var actual = EventTypes().Select(t => t.Name).ToHashSet();

        Assert.All(expected, name => Assert.Contains(name, actual));
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void Event_lives_in_Domain_Events_and_is_a_sealed_record(Type eventType)
    {
        Assert.Equal("LibraryApp.Domain.Events", eventType.Namespace);
        Assert.True(eventType.IsSealed, $"{eventType.Name} must be sealed");
        Assert.NotNull(eventType.GetMethod("<Clone>$"));   // compiler marker of a record
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void Event_carries_only_ids_and_simple_values(Type eventType)
    {
        foreach (var property in eventType.GetProperties())
        {
            var type = property.PropertyType;
            var simple = AllowedTypes.Contains(type) || type.IsEnum || Nullable.GetUnderlyingType(type)?.IsEnum == true;
            Assert.True(simple, $"{eventType.Name}.{property.Name} is {type.Name}: events carry ids and simple values only");
        }
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void Time_fields_are_UTC_by_name(Type eventType)
    {
        foreach (var property in eventType.GetProperties().Where(p => p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?)))
            Assert.EndsWith("Utc", property.Name);
    }

    [Fact]
    public void Event_names_are_unique()
    {
        var names = EventTypes().Select(t => t.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
