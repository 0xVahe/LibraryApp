using FluentValidation;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Validators;

/// <summary>Login input (SD-15, MEM-03). Registered automatically by AddValidatorsFromAssembly in AddPlatform.</summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator(CorporateEmailPolicy emailPolicy)
    {
        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress()
            .Must(emailPolicy.IsCorporate)
            .WithErrorCode(StaffErrors.EmailNotCorporate.Code)
            .WithMessage(StaffErrors.EmailNotCorporate.Message);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MaximumLength(128);
    }
}