using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Tests.Staff;

public class PasswordChangeServiceTests
{
    private readonly PasswordChangeFake _fake = new();
    private readonly FakeCurrentUser _caller = new();
    private readonly TestClock _clock = new(new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc));

    private PasswordChangeService Service => new(_fake, _fake, _fake, _fake, _clock, _caller);

    private static ChangePasswordRequest Request => new("Passw0rd1", "NewPassw0rd2");

    [Fact]
    public async Task Right_current_password_changes_the_password_and_publishes_one_event()
    {
        await Service.ChangeAsync(Request);

        Assert.True(_fake.Changed);
        Assert.Equal(1, _fake.Transactions);
        var published = Assert.IsType<StaffPasswordChanged>(Assert.Single(_fake.Published));
        Assert.Equal(_caller.StaffMemberId, published.StaffMemberId);
        Assert.Equal(_clock.UtcNow, published.OccurredAtUtc);
    }

    [Fact]
    public async Task Wrong_current_password_changes_nothing()
    {
        _fake.VerifyResult = false;

        var ex = await Assert.ThrowsAsync<DomainException>(() => Service.ChangeAsync(Request));

        Assert.Equal("staff.password_change_failed", ex.Error.Code);
        Assert.False(_fake.Changed);
        Assert.Equal(0, _fake.Transactions);
        Assert.Empty(_fake.Published);
    }

    [Fact]
    public async Task Rejected_new_password_publishes_nothing()
    {
        _fake.ChangeResult = false;

        var ex = await Assert.ThrowsAsync<DomainException>(() => Service.ChangeAsync(Request));

        Assert.Equal("staff.password_change_failed", ex.Error.Code);
        Assert.Empty(_fake.Published);
    }

    [Fact]
    public async Task Caller_without_staff_member_id_is_rejected()
    {
        _caller.StaffMemberId = null;

        var ex = await Assert.ThrowsAsync<DomainException>(() => Service.ChangeAsync(Request));

        Assert.Equal("staff.password_change_failed", ex.Error.Code);
        Assert.False(_fake.Verified);
    }
}

internal sealed class PasswordChangeFake : IUserAccountStore, IUnitOfWork, IEventBus, IIdGenerator
{
    public bool VerifyResult { get; set; } = true;
    public bool ChangeResult { get; set; } = true;
    public bool Verified { get; private set; }
    public bool Changed { get; private set; }
    public int Transactions { get; private set; }
    public List<IDomainEvent> Published { get; } = [];

    public Task<LoginAccount?> VerifyCredentialsAsync(string email, string password, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<bool> VerifyCurrentPasswordAsync(Guid userId, string currentPassword, CancellationToken ct)
    {
        Verified = true;
        return Task.FromResult(VerifyResult);
    }

    public Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        Changed = ChangeResult;
        return Task.FromResult(ChangeResult);
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        Transactions++;
        return operation(ct);
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        Transactions++;
        return operation(ct);
    }

    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : class, IDomainEvent
    {
        Published.Add(domainEvent);
        return Task.CompletedTask;
    }

    public Guid NewId() => Guid.NewGuid();
}