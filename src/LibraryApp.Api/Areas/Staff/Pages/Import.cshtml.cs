using System.Text;
using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Exceptions;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;

namespace LibraryApp.Api.Areas.Staff.Pages;

/// <summary>The CSV form has its own page so enrolment validation cannot reject an unrelated file upload.</summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
[RequestSizeLimit(1_100_000)]
[RequestFormLimits(MultipartBodyLengthLimit = 1_100_000)]
public sealed class ImportModel(IStaffImportService import, IMemoryCache cache, ICurrentUser currentUser) : PageModel
{
    private const string ReceiptKey = "staff.import.receipt";

    [BindProperty] public IFormFile? CsvFile { get; set; }

    public IReadOnlyList<StaffImportError> Errors { get; private set; } = [];
    public IReadOnlyList<StaffImportMemberResult> Imported { get; private set; } = [];

    public void OnGet()
    {
        Response.Headers.CacheControl = "no-store";
        // TempData carries only a random reference. Passwords stay on the server until this one-time GET.
        if (TempData[ReceiptKey] is string receipt)
        {
            var key = (ReceiptKey, currentUser.UserId, receipt);
            if (cache.TryGetValue<StaffImportResult>(key, out var result) && result is not null)
            {
                cache.Remove(key);
                Imported = result.Members;
            }
        }
    }

    public IActionResult OnGetTemplate() => File(Encoding.UTF8.GetBytes(
        "EmployeeId,FirstName,LastName,CorporateEmail,Department,Position,LineManagerEmail,Category,Role\r\n"),
        "text/csv; charset=utf-8", "staff-import-template.csv");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ModelState.IsValid)
            return Page();

        if (CsvFile is null || CsvFile.Length == 0)
            return FileError("Choose a non-empty CSV file.");
        if (CsvFile.Length > StaffImportLimits.MaximumFileBytes)
            return FileError("The file must be 1 MB or smaller.");

        try
        {
            await using var stream = CsvFile.OpenReadStream();
            var result = await import.ImportAsync(stream, ct);
            var receipt = Guid.NewGuid().ToString("N");
            cache.Set((ReceiptKey, currentUser.UserId, receipt), result, TimeSpan.FromMinutes(5));
            TempData[ReceiptKey] = receipt;
            TempData.SetAlert(AlertKind.Green, $"{result.Members.Count} staff members imported.");
            return RedirectToPage("./Import");
        }
        catch (StaffImportException exception)
        {
            Errors = exception.Errors;
            return Page();
        }
        catch (StaffCsvFormatException exception)
        {
            Errors = [new(exception.RowNumber, exception.Field, exception.Message)];
            return Page();
        }
        catch (DomainException exception)
        {
            return this.DomainError(exception);
        }
    }

    private IActionResult FileError(string message)
    {
        Errors = [new(1, "File", message)];
        return Page();
    }
}
