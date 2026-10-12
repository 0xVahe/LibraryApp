namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>
/// The data a librarian provides to issue a book (BOR-01).
/// </summary>
/// <param name="StaffMemberId">The identifier of the employee who borrows the book.</param>
/// <param name="Barcode">The barcode printed on the physical copy. Case and spaces are ignored by Catalog.</param>
public sealed record IssueLoanRequest(
    Guid StaffMemberId,
    string Barcode);