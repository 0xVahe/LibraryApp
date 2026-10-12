using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Validators;

namespace LibraryApp.Application.Tests.Borrowing;

public sealed class IssueLoanRequestValidatorTests
{
    private readonly IssueLoanRequestValidator _validator = new();

    [Fact]
    public void A_staff_member_and_a_barcode_are_valid()
    {
        Assert.True(_validator.Validate(new IssueLoanRequest(Guid.NewGuid(), " bk-0001-01 ")).IsValid);
    }

    [Fact]
    public void An_empty_staff_member_id_is_refused()
    {
        var result = _validator.Validate(new IssueLoanRequest(Guid.Empty, "BK-0001-01"));

        Assert.Equal(nameof(IssueLoanRequest.StaffMemberId), Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BK-0001-01-0000000000000000000000")]   // 33 characters
    public void A_missing_or_too_long_barcode_is_refused(string barcode)
    {
        var result = _validator.Validate(new IssueLoanRequest(Guid.NewGuid(), barcode));

        Assert.Equal(nameof(IssueLoanRequest.Barcode), Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void Spaces_around_a_32_character_barcode_are_not_counted()
    {
        Assert.True(_validator.Validate(new IssueLoanRequest(Guid.NewGuid(), "  " + new string('A', 32) + "  ")).IsValid);
    }
}
