using System.Reflection;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Tests.Borrowing;

/// <summary>
/// The numbers are stored in the database and the index filters and CHECK constraints use them
/// ([Status] IN (1, 2), [Status] = 1, [Status] <> 3): if one changes, existing rows and constraints change meaning.
/// </summary>
public class BorrowingEnumValuesTests
{
    [Theory]
    [InlineData(LoanStatus.Active, 1)]
    [InlineData(LoanStatus.Overdue, 2)]
    [InlineData(LoanStatus.Returned, 3)]
    [InlineData(LoanStatus.Lost, 4)]
    public void LoanStatus_HasTheAgreedNumber(LoanStatus status, int number)
    {
        Assert.Equal(number, (int)status);
    }

    [Theory]
    [InlineData(ReservationStatus.Waiting, 1)]
    [InlineData(ReservationStatus.ReadyForPickup, 2)]
    [InlineData(ReservationStatus.Fulfilled, 3)]
    [InlineData(ReservationStatus.Cancelled, 4)]
    [InlineData(ReservationStatus.Expired, 5)]
    public void ReservationStatus_HasTheAgreedNumber(ReservationStatus status, int number)
    {
        Assert.Equal(number, (int)status);
    }

    [Theory]
    [InlineData(PenaltyRecordStatus.Active, 1)]
    [InlineData(PenaltyRecordStatus.Expired, 2)]
    [InlineData(PenaltyRecordStatus.Waived, 3)]
    public void PenaltyRecordStatus_HasTheAgreedNumber(PenaltyRecordStatus status, int number)
    {
        Assert.Equal(number, (int)status);
    }

    [Fact]
    public void EveryBorrowingErrorCode_StartsWithTheModulePrefix()
    {
        var errors = typeof(BorrowingErrors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Error))
            .Select(f => (Error)f.GetValue(null)!)
            .ToList();

        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.StartsWith("borrowing.", error.Code));
    }
}
