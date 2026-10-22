namespace LibraryApp.Application.Staff.Options;

/// <summary>Staff business rules. Values are required in configuration and take effect on restart.</summary>
public sealed class StaffOptions
{
    public const string SectionName = "Staff";

    public StaffImportOptions Import { get; set; } = new();
    public RestrictedStaffLimitsOptions RestrictedLimits { get; set; } = new();
}

public sealed class StaffImportOptions
{
    public int MaxRows { get; set; }
    public long MaxFileBytes { get; set; }
}

public sealed class RestrictedStaffLimitsOptions
{
    public int MaxLoans { get; set; }
}
