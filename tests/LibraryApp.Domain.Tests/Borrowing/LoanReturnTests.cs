using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using Xunit;

namespace LibraryApp.Domain.Tests.Borrowing;

public sealed class LoanReturnTests
{
    private static readonly DateTime IssuedAtUtc =
        new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static Loan CreateLoan() =>
        Loan.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            IssuedAtUtc,
            14);

    [Fact]
    public void Return_closes_loan_and_trims_note()
    {
        Loan loan = CreateLoan();
        DateTime returnedAtUtc = IssuedAtUtc.AddDays(2);

        loan.Return(returnedAtUtc, "  Cover checked.  ");

        Assert.Equal(LoanStatus.Returned, loan.Status);
        Assert.Equal(returnedAtUtc, loan.ReturnedAtUtc);
        Assert.Equal("Cover checked.", loan.ReturnNote);
        Assert.False(loan.IsOpen);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Return_stores_empty_note_as_null(string? note)
    {
        Loan loan = CreateLoan();

        loan.Return(IssuedAtUtc.AddDays(2), note);

        Assert.Null(loan.ReturnNote);
    }

    [Fact]
    public void Return_accepts_maximum_length_after_trimming()
    {
        Loan loan = CreateLoan();
        string note = new('a', Loan.ReturnNoteMaxLength);

        loan.Return(IssuedAtUtc.AddDays(2), $"  {note}  ");

        Assert.Equal(note, loan.ReturnNote);
    }

    [Fact]
    public void Return_rejects_long_note_without_changing_loan()
    {
        Loan loan = CreateLoan();
        string note = new('a', Loan.ReturnNoteMaxLength + 1);

        var ex = Assert.Throws<DomainException>(() =>
            loan.Return(IssuedAtUtc.AddDays(2), note));
        Assert.Equal(BorrowingErrors.ReturnNoteTooLong, ex.Error);

        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Null(loan.ReturnedAtUtc);
        Assert.Null(loan.ReturnNote);
    }

    [Fact]
    public void Return_cannot_overwrite_a_previous_return()
    {
        Loan loan = CreateLoan();
        DateTime returnedAtUtc = IssuedAtUtc.AddDays(2);
        loan.Return(returnedAtUtc, "Original note.");

        var exception = Assert.Throws<DomainException>(() =>
            loan.Return(returnedAtUtc.AddDays(1), "Replacement note."));

        Assert.Equal(BorrowingErrors.LoanAlreadyClosed, exception.Error);
        Assert.Equal(returnedAtUtc, loan.ReturnedAtUtc);
        Assert.Equal("Original note.", loan.ReturnNote);
    }
}