using LibraryApp.Application.Abstractions.Common;

namespace LibraryApp.Application.Tests.Borrowing.Fakes;

public sealed class FakeIdGenerator : IIdGenerator
{
    public Guid Id { get; } =
        new("10000000-0000-0000-0000-000000000001");

    public int Calls { get; private set; }

    public Guid NewId()
    {
        Calls++;
        return Id;
    }
}