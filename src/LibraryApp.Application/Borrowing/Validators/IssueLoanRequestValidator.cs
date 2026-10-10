using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Borrowing.Validators;

/// <summary>Validates the input required to issue a loan.</summary>
public static class IssueLoanRequestValidator
{
    public static void Validate(IssueLoanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.StaffMemberId == Guid.Empty)
            throw new DomainException(BorrowingErrors.InvalidReferenceId);

        if (string.IsNullOrWhiteSpace(request.Barcode))
            throw new DomainException(BorrowingErrors.BarcodeRequired);
    }
}