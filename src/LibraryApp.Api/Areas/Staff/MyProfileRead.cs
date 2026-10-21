using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Api.Areas.Staff;

/// <summary>Reads only the caller's profile, once per request, without loading their Borrowing summary.</summary>
internal static class MyProfileRead
{
    private static readonly object Key = new();

    public static Task<MyProfileDto> GetAsync(HttpContext context, IStaffManagementService staff, ICurrentUser caller)
    {
        if (context.Items[Key] is Task<MyProfileDto> read)
            return read;

        var memberId = caller.StaffMemberId ?? throw new DomainException(StaffErrors.NotFound);
        read = LoadAsync(staff, memberId, context.RequestAborted);
        context.Items[Key] = read;
        return read;
    }

    private static async Task<MyProfileDto> LoadAsync(IStaffManagementService staff, Guid memberId, CancellationToken ct)
    {
        var member = await staff.GetAsync(memberId, ct);
        return new MyProfileDto(member.Id, member.FirstName, member.LastName, member.CorporateEmail,
            member.DepartmentName, member.Position, member.StaffCategoryName, member.Status);
    }
}
