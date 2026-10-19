using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Api.Areas.Staff.Pages;

/// <summary>The editable fields and form context shared by enrolment and editing.</summary>
public sealed record StaffProfileFieldsModel(UpdateStaffMemberRequest Input, StaffFormPage Form);