using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Services;

public interface IStaffImportService
{
    Task<StaffImportResult> ImportAsync(Stream csv, CancellationToken ct = default);
}
