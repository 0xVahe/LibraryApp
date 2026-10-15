using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Ports;

/// <summary>Parses the HR export format behind an Infrastructure adapter.</summary>
public interface IStaffCsvParser
{
    Task<IReadOnlyList<StaffImportCsvRow>> ParseAsync(Stream csv, CancellationToken ct = default);
}
