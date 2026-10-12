using FluentValidation;
using LibraryApp.Application.Borrowing.Dto;

namespace LibraryApp.Application.Borrowing.Validators;

/// <summary>Input of a reservation (BOR-07). Who the reservation is for is decided by the service from the caller's role.</summary>
public sealed class CreateReservationRequestValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationRequestValidator()
    {
        RuleFor(x => x.BookId).NotEmpty();
        RuleFor(x => x.StaffMemberId).NotEqual(Guid.Empty);
    }
}
