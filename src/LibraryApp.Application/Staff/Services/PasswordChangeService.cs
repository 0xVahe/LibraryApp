using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Services;

public sealed class PasswordChangeService(
    IUserAccountStore accounts,
    IUnitOfWork unitOfWork,
    IEventBus events,
    IIdGenerator ids,
    IClock clock,
    ICurrentUser currentUser) : IPasswordChangeService
{
    public async Task ChangeAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        // Every login token has staff_member_id (MEM-02); same error as a wrong password otherwise.
        var staffMemberId = currentUser.StaffMemberId ?? throw new DomainException(StaffErrors.PasswordChangeFailed);
        var currentPassword = request.CurrentPassword ?? string.Empty;

        // Outside the transaction, so a failed attempt stays counted even though nothing else is saved.
        if (!await accounts.VerifyCurrentPasswordAsync(currentUser.UserId, currentPassword, ct))
            throw new DomainException(StaffErrors.PasswordChangeFailed);

        var changed = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (!await accounts.ChangePasswordAsync(currentUser.UserId, currentPassword,
                    request.NewPassword ?? string.Empty, token))
                return false;

            await events.PublishAsync(new StaffPasswordChanged(ids.NewId(), clock.UtcNow, staffMemberId), token);
            return true;
        }, ct);

        if (!changed)
            throw new DomainException(StaffErrors.PasswordChangeFailed);
    }
}