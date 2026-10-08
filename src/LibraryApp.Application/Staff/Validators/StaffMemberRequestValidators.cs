using FluentValidation;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Validators;

public sealed class EnrolStaffMemberRequestValidator : AbstractValidator<EnrolStaffMemberRequest>
{
    public EnrolStaffMemberRequestValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CorporateEmail).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Position).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.StaffCategoryId).NotEmpty();
        RuleFor(x => x.LineManagerEmail).MaximumLength(256).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.LineManagerEmail));
        RuleFor(x => x.Password).Cascade(CascadeMode.Stop).NotEmpty().MinimumLength(8).MaximumLength(128)
            .Must(x => x.Any(char.IsLetter) && x.Any(c => c is >= '0' and <= '9'))
            .WithMessage("Passwords must have at least one digit and one letter.");
        RuleFor(x => x.Role).Must(x => x is null || StaffRoles.All.Contains(x))
            .WithMessage("Role must be StaffMember, Librarian or LibraryAdministrator.");
    }
}

public sealed class UpdateStaffMemberRequestValidator : AbstractValidator<UpdateStaffMemberRequest>
{
    public UpdateStaffMemberRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CorporateEmail).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Position).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.StaffCategoryId).NotEmpty();
        RuleFor(x => x.LineManagerEmail).MaximumLength(256).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.LineManagerEmail));
    }
}
