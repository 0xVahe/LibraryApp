using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Staff.Services;

/// <summary>Staff enrollment and profile changes; account, member and event writes share one transaction.</summary>
public sealed class StaffManagementService(
    IStaffManagementRepository members,
    IStaffAccountStore accounts,
    IUnitOfWork unitOfWork,
    IEventBus events,
    IClock clock,
    IIdGenerator ids,
    CorporateEmailPolicy emailPolicy)
    : IStaffManagementService
{
    public async Task<StaffMemberDto> EnrolAsync(
        EnrolStaffMemberRequest request, bool callerIsAdministrator, CancellationToken ct = default)
    {
        RequireCorporateEmail(request.CorporateEmail);

        var role = request.Role ?? StaffRoles.StaffMember;
        if (role != StaffRoles.StaffMember && !callerIsAdministrator)
            throw new DomainException(StaffErrors.RoleAssignmentForbidden);

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var email = request.CorporateEmail.Trim().ToLowerInvariant();
            var employeeId = request.EmployeeId.Trim();
            if (await members.EmployeeIdExistsAsync(employeeId, token))
                throw new DomainException(StaffErrors.EmployeeIdTaken);

            await RequireUniqueEmailAsync(email, null, null, token);
            var (department, category) = await ReferenceNamesAsync(request.DepartmentId, request.StaffCategoryId, token);

            var userId = ids.NewId();
            await accounts.CreateAsync(userId, email, request.Password, role, token);
            var member = new StaffMember(ids.NewId(), userId, employeeId, request.FirstName, request.LastName,
                email, request.DepartmentId, request.Position, request.LineManagerEmail, request.StaffCategoryId, clock.UtcNow);
            members.Add(member);

            await events.PublishAsync(new StaffEnrolled(ids.NewId(), clock.UtcNow, member.Id), token);
            return ToDto(member, department, category, role);
        }, ct);
    }

    public async Task<StaffMemberDto> GetAsync(Guid id, CancellationToken ct = default) =>
        await members.GetDetailsAsync(id, ct) ?? throw new DomainException(StaffErrors.NotFound);

    public async Task<StaffMemberDto> UpdateAsync(Guid id, UpdateStaffMemberRequest request, CancellationToken ct = default)
    {
        RequireCorporateEmail(request.CorporateEmail);

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var member = await members.GetByIdAsync(id, token) ?? throw new DomainException(StaffErrors.NotFound);
            if (member.Status == StaffMemberStatus.Deactivated)
                throw new DomainException(StaffErrors.DeactivatedProfileLocked);

            var email = request.CorporateEmail.Trim().ToLowerInvariant();
            await RequireUniqueEmailAsync(email, id, member.UserId, token);
            var (department, category) = await ReferenceNamesAsync(request.DepartmentId, request.StaffCategoryId, token);
            if (email != member.CorporateEmail)
                await accounts.UpdateEmailAsync(member.UserId, email, token);

            member.UpdateProfile(request.FirstName, request.LastName, email, request.Position, request.LineManagerEmail);
            member.MoveToDepartment(request.DepartmentId);
            member.ChangeCategory(request.StaffCategoryId);

            return ToDto(member, department, category, await accounts.GetRoleAsync(member.UserId, token));
        }, ct);
    }

    private void RequireCorporateEmail(string email)
    {
        if (!emailPolicy.IsCorporate(email))
            throw new DomainException(StaffErrors.EnrolEmailNotCorporate);
    }

    private async Task RequireUniqueEmailAsync(string email, Guid? memberId, Guid? userId, CancellationToken ct)
    {
        if (await members.EmailExistsAsync(email, memberId, ct) || await accounts.EmailExistsAsync(email, userId, ct))
            throw new DomainException(StaffErrors.EmailTaken);
    }

    private async Task<(string Department, string Category)> ReferenceNamesAsync(Guid departmentId, Guid categoryId, CancellationToken ct)
    {
        var department = await members.GetDepartmentNameAsync(departmentId, ct)
                         ?? throw new DomainException(StaffErrors.DepartmentInvalid);
        var category = await members.GetCategoryNameAsync(categoryId, ct)
                       ?? throw new DomainException(StaffErrors.CategoryNotFound);
        return (department, category);
    }

    private static StaffMemberDto ToDto(StaffMember member, string department, string category, string role) =>
        new(member.Id, member.EmployeeId, member.FirstName, member.LastName, member.CorporateEmail,
            member.DepartmentId, department, member.Position, member.LineManagerEmail,
            member.StaffCategoryId, category, member.Status.ToString(), member.JoinedLibraryAtUtc, role);

    public Task<PagedResult<StaffMemberListItemDto>> SearchAsync(StaffMemberSearchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Paging);

        string? text = Normalize(request.Search);
        string? statusText = Normalize(request.Status);
        StaffMemberStatus? status = null;

        if (statusText is not null)
        {
            if (!Enum.TryParse<StaffMemberStatus>(statusText, ignoreCase: true, out var parsedStatus) ||
                !Enum.IsDefined(typeof(StaffMemberStatus), parsedStatus) ||
                !string.Equals(statusText, parsedStatus.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException(StaffErrors.StatusInvalid);
            }

            status = parsedStatus;
        }

        var filter = new StaffMemberSearchFilter(text, request.DepartmentId, status);

        return members.SearchAsync(filter, request.Paging, ct);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
