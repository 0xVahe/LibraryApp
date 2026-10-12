namespace LibraryApp.Application.Staff.Exceptions;

public sealed class StaffCsvFormatException(int rowNumber, string field, string message)
    : Exception(message)
{
    public int RowNumber { get; } = rowNumber;
    public string Field { get; } = field;
}
