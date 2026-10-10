using FluentValidation;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Validators;

public sealed class UpsertStaffCategoryRequestValidator
    : AbstractValidator<UpsertStaffCategoryRequest>
{
    public UpsertStaffCategoryRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .WithErrorCode(StaffErrors.CategoryNameRequired.Code)
            .WithMessage(StaffErrors.CategoryNameRequired.Message)
            .MaximumLength(100);

        RuleFor(request => request.MaxLoans)
            .GreaterThan(0)
            .WithErrorCode(StaffErrors.MaxLoansInvalid.Code)
            .WithMessage(StaffErrors.MaxLoansInvalid.Message);

        RuleFor(request => request.LoanDays)
            .GreaterThan(0)
            .WithErrorCode(StaffErrors.LoanDaysInvalid.Code)
            .WithMessage(StaffErrors.LoanDaysInvalid.Message);

        RuleFor(request => request.MaxRenewals)
            .GreaterThanOrEqualTo(0)
            .WithErrorCode(StaffErrors.MaxRenewalsInvalid.Code)
            .WithMessage(StaffErrors.MaxRenewalsInvalid.Message);
    }
}