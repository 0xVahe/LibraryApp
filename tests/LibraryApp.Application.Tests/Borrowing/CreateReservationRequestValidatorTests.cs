using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Validators;

namespace LibraryApp.Application.Tests.Borrowing;

public sealed class CreateReservationRequestValidatorTests
{
    private readonly CreateReservationRequestValidator _validator = new();

    [Fact]
    public void A_book_with_or_without_a_staff_member_is_valid()
    {
        Assert.True(_validator.Validate(new CreateReservationRequest(Guid.NewGuid())).IsValid);
        Assert.True(_validator.Validate(new CreateReservationRequest(Guid.NewGuid(), Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void An_empty_book_id_is_refused()
    {
        var result = _validator.Validate(new CreateReservationRequest(Guid.Empty));

        Assert.Equal(nameof(CreateReservationRequest.BookId), Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void An_empty_staff_member_id_is_refused()
    {
        var result = _validator.Validate(new CreateReservationRequest(Guid.NewGuid(), Guid.Empty));

        Assert.Equal(nameof(CreateReservationRequest.StaffMemberId), Assert.Single(result.Errors).PropertyName);
    }
}
