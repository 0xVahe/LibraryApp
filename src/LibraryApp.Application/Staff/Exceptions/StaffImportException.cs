using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Exceptions;

public sealed class StaffImportException(IReadOnlyList<StaffImportError> errors)
    : Exception("The staff CSV contains invalid rows.")
{
    public IReadOnlyList<StaffImportError> Errors { get; } = errors;
}
