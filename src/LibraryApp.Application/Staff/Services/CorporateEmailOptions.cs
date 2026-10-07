namespace LibraryApp.Application.Staff.Services;

/// <summary>MEM-03: the corporate domain is configurable, default ameriabank.am.</summary>
public sealed class CorporateEmailOptions
{
    public const string SectionName = "CorporateEmail";

    public string Domain { get; set; } = "ameriabank.am";
}