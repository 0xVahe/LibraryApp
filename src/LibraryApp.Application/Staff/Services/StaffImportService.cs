using System.Security.Cryptography;
using FluentValidation;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Exceptions;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Services;

public sealed class StaffImportService(
    IStaffCsvParser parser,
    IStaffManagementService staff,
    IValidator<EnrolStaffMemberRequest> validator,
    CorporateEmailPolicy emailPolicy,
    IStaffManagementRepository members,
    IStaffAccountStore accounts,
    ICurrentUser currentUser) : IStaffImportService
{
    public const int MaximumRows = StaffImportLimits.MaximumRows;

    public async Task<StaffImportResult> ImportAsync(Stream csv, CancellationToken ct = default)
    {
        var rows = await parser.ParseAsync(csv, ct);
        if (rows.Count > MaximumRows)
            throw new StaffCsvFormatException(rows[MaximumRows].RowNumber, "File", $"A file may contain at most {MaximumRows} data rows.");
        if (rows.Count == 0)
            throw new StaffCsvFormatException(2, "File", "The CSV must contain at least one staff row.");

        var errors = new List<StaffImportError>();
        var employees = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var emails = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var passwords = new Dictionary<int, string>();
        var prepared = new List<EnrolStaffMemberRequest>();

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var password = CreateTemporaryPassword();
            passwords[row.RowNumber] = password;
            var request = new EnrolStaffMemberRequest(
                row.EmployeeId, row.FirstName, row.LastName, row.CorporateEmail,
                Guid.NewGuid(), row.Position, row.LineManagerEmail, Guid.NewGuid(), password, row.Role);

            var validation = await validator.ValidateAsync(request, ct);
            foreach (var failure in validation.Errors)
                errors.Add(new StaffImportError(row.RowNumber, failure.PropertyName, failure.ErrorMessage));

            if (!emailPolicy.IsCorporate(row.CorporateEmail))
                errors.Add(new StaffImportError(row.RowNumber, nameof(row.CorporateEmail), StaffErrors.EnrolEmailNotCorporate.Message));

            var employeeId = row.EmployeeId.Trim();
            if (employees.TryGetValue(employeeId, out var previousEmployeeRow))
                errors.Add(new StaffImportError(row.RowNumber, nameof(row.EmployeeId), $"Employee ID duplicates row {previousEmployeeRow}."));
            else
                employees[employeeId] = row.RowNumber;

            var email = row.CorporateEmail.Trim().ToLowerInvariant();
            if (emails.TryGetValue(email, out var previousEmailRow))
                errors.Add(new StaffImportError(row.RowNumber, nameof(row.CorporateEmail), $"Corporate email duplicates row {previousEmailRow}."));
            else
                emails[email] = row.RowNumber;

            var role = row.Role ?? StaffRoles.StaffMember;
            if ((role is StaffRoles.Librarian or StaffRoles.LibraryAdministrator) &&
                !currentUser.IsInRole(StaffRoles.LibraryAdministrator))
                errors.Add(new StaffImportError(row.RowNumber, nameof(row.Role), "Only a LibraryAdministrator may assign Librarian or LibraryAdministrator."));
        }

        var departmentNames = rows.Select(row => row.Department.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var categoryNames = rows.Select(row => row.Category.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var departments = (await members.GetDepartmentsByNamesAsync(departmentNames, ct))
            .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var categories = (await members.GetCategoriesByNamesAsync(categoryNames, ct))
            .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var employeeIds = rows.Select(row => row.EmployeeId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var corporateEmails = rows.Select(row => row.CorporateEmail.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existingEmployeeIds = (await members.GetExistingEmployeeIdsAsync(employeeIds, ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingProfileEmails = (await members.GetExistingEmailsAsync(corporateEmails, ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingAccountEmails = (await accounts.GetExistingEmailsAsync(corporateEmails, ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var employeeId = row.EmployeeId.Trim();
            if (existingEmployeeIds.Contains(employeeId))
                errors.Add(new StaffImportError(row.RowNumber, nameof(row.EmployeeId), StaffErrors.EmployeeIdTaken.Message));

            var email = row.CorporateEmail.Trim().ToLowerInvariant();
            if (existingProfileEmails.Contains(email) || existingAccountEmails.Contains(email))
                errors.Add(new StaffImportError(row.RowNumber, nameof(row.CorporateEmail), StaffErrors.EmailTaken.Message));

            if (!departments.TryGetValue(row.Department.Trim(), out var department))
                errors.Add(new StaffImportError(row.RowNumber, nameof(row.Department), StaffErrors.DepartmentInvalid.Message));
            if (!categories.TryGetValue(row.Category.Trim(), out var category))
                errors.Add(new StaffImportError(row.RowNumber, nameof(row.Category), StaffErrors.CategoryNotFound.Message));

            if (department is not null && category is not null)
            {
                var request = new EnrolStaffMemberRequest(row.EmployeeId, row.FirstName, row.LastName, row.CorporateEmail,
                    department.Id, row.Position, row.LineManagerEmail, category.Id, passwords[row.RowNumber], row.Role);
                prepared.Add(request);
            }
        }

        if (errors.Count > 0)
            throw new StaffImportException(errors);

        var enrolled = await staff.EnrolBatchAsync(prepared, ct);
        return new StaffImportResult(enrolled.Select((member, index) =>
            new StaffImportMemberResult(member, prepared[index].Password)).ToArray());
    }

    private static string CreateTemporaryPassword()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return $"A{Convert.ToBase64String(bytes).Replace('+', 'A').Replace('/', 'B').TrimEnd('=')}9";
    }
}
