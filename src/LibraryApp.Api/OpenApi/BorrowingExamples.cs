using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Common;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.OpenApi;

/// <summary>
/// Request and response examples of the Borrowing endpoints (NFR-11), taken from the Development seed: the requests work on
/// a freshly seeded database and each response is one seeded record (checked by <c>BorrowingSwaggerTests</c>).
/// </summary>
public sealed class BorrowingExamples : ISchemaFilter
{
    /// <summary>The member and the copy of the issue example; the member and the book of the reservation example.</summary>
    public static readonly Guid IssueStaffMember = Members.ElenDanielyan;
    public static readonly string IssueBarcode = CatalogSeedIds.CopyBarcode(5, 1);
    public static readonly Guid ReservationStaffMember = Members.SofiaHambardzumyan;
    public static readonly Guid ReservationBook = CatalogSeedIds.Book(1);

    /// <summary>The seeded records the response examples show.</summary>
    public static readonly Guid OpenLoan = BorrowingSeedIds.Loan(1);
    public static readonly Guid ReturnedLoan = BorrowingSeedIds.Loan(9);
    public static readonly Guid LostLoan = BorrowingSeedIds.Loan(6);
    public static readonly Guid WaivedPenalty = BorrowingSeedIds.Penalty(4);
    public static readonly Guid WaitingReservation = BorrowingSeedIds.Reservation(1);

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        OpenApiObject? example = context.Type switch
        {
            Type type when type == typeof(IssueLoanRequest) => new OpenApiObject
            {
                ["staffMemberId"] = Id(IssueStaffMember),
                ["barcode"] = new OpenApiString(IssueBarcode),
            },
            Type type when type == typeof(ReturnLoanRequest) => new OpenApiObject
            {
                ["damaged"] = new OpenApiBoolean(false),
                ["note"] = new OpenApiString("Cover slightly worn, no damage."),
            },
            Type type when type == typeof(CreateReservationRequest) => new OpenApiObject
            {
                ["bookId"] = Id(ReservationBook),
                ["staffMemberId"] = Id(ReservationStaffMember),
            },
            Type type when type == typeof(WaivePenaltyRequest) => new OpenApiObject
            {
                ["reason"] = new OpenApiString("Late because of a business trip"),
            },
            Type type when type == typeof(LoanDto) => new OpenApiObject
            {
                ["loanId"] = Id(OpenLoan),
                ["bookCopyId"] = Id(CatalogSeedIds.Copy(1, 1)),
                ["staffMemberId"] = Id(Members.AnnaPetrosyan),
                ["issuedAtUtc"] = Utc("2026-10-15T06:30:00Z"),
                ["dueAtUtc"] = Utc("2026-11-05T06:30:00Z"),
                ["renewalCount"] = new OpenApiInteger(0),
                ["status"] = new OpenApiString("Active"),
            },
            Type type when type == typeof(ReturnLoanResponse) => new OpenApiObject
            {
                ["loanId"] = Id(ReturnedLoan),
                ["returnedAtUtc"] = Utc("2026-08-27T07:15:00Z"),
                ["status"] = new OpenApiString("Returned"),
                ["overdueWorkingDays"] = new OpenApiInteger(3),
                ["penaltyPointsApplied"] = new OpenApiInteger(3),
            },
            Type type when type == typeof(DeclareLostResponse) => new OpenApiObject
            {
                ["loanId"] = Id(LostLoan),
                ["declaredAtUtc"] = Utc("2026-09-20T08:00:00Z"),
                ["status"] = new OpenApiString("Lost"),
                ["penaltyPointsApplied"] = new OpenApiInteger(10),
            },
            Type type when type == typeof(LoanHistoryItemDto) => HistoryItem(),
            Type type when type == typeof(PagedResult<LoanHistoryItemDto>) => Page(HistoryItem()),
            Type type when type == typeof(ReservationDto) => new OpenApiObject
            {
                ["reservationId"] = Id(WaitingReservation),
                ["bookId"] = Id(CatalogSeedIds.Book(1)),
                ["staffMemberId"] = Id(Members.AramSargsyan),
                ["createdAtUtc"] = Utc("2026-10-19T06:30:00Z"),
                ["status"] = new OpenApiString("Waiting"),
                ["queuePosition"] = new OpenApiInteger(1),
                ["copyId"] = new OpenApiNull(),
                ["readyUntilUtc"] = new OpenApiNull(),
            },
            Type type when type == typeof(PenaltyDto) => PenaltyRecord(),
            Type type when type == typeof(PagedResult<PenaltyDto>) => Page(PenaltyRecord()),
            _ => null,
        };

        if (example is not null)
            schema.Example = example;
    }

    /// <summary>A page of one item, as every list endpoint returns it (<see cref="PagedResult{T}"/>).</summary>
    private static OpenApiObject Page(OpenApiObject item) => new()
    {
        ["items"] = new OpenApiArray { item },
        ["totalCount"] = new OpenApiInteger(1),
        ["page"] = new OpenApiInteger(1),
        ["pageSize"] = new OpenApiInteger(PageRequest.DefaultPageSize),
        ["totalPages"] = new OpenApiInteger(1),
    };

    /// <summary>Loan 9: Anna's copy 3 of book 16, issued for 21 days and returned 3 working days late.</summary>
    private static OpenApiObject HistoryItem() => new()
    {
        ["loanId"] = Id(ReturnedLoan),
        ["bookCopyId"] = Id(CatalogSeedIds.Copy(16, 3)),
        ["staffMemberId"] = Id(Members.AnnaPetrosyan),
        ["issuedAtUtc"] = Utc("2026-08-03T06:30:00Z"),
        ["dueAtUtc"] = Utc("2026-08-24T06:30:00Z"),
        ["returnedAtUtc"] = Utc("2026-08-27T07:15:00Z"),
        ["renewalCount"] = new OpenApiInteger(0),
        ["status"] = new OpenApiString("Returned"),
    };

    /// <summary>Penalty 4: the 3 points of Anna's late return of loan 9, waived by a librarian.</summary>
    private static OpenApiObject PenaltyRecord() => new()
    {
        ["penaltyId"] = Id(WaivedPenalty),
        ["staffMemberId"] = Id(Members.AnnaPetrosyan),
        ["loanId"] = Id(ReturnedLoan),
        ["points"] = new OpenApiInteger(3),
        ["reason"] = new OpenApiString("Returned 3 working days late"),
        ["status"] = new OpenApiString("Waived"),
        ["createdAtUtc"] = Utc("2026-08-27T07:15:00Z"),
        ["expiresAtUtc"] = Utc("2026-11-25T07:15:00Z"),
        ["waivedReason"] = new OpenApiString("Late because of a business trip"),
    };

    private static OpenApiString Id(Guid id) => new(id.ToString());

    private static OpenApiString Utc(string value) => new(value);
}
