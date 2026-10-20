using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LibraryApp.Api.OpenApi;

/// <summary>
/// Request and response examples of the Borrowing endpoints (NFR-11), built from the seed: a request copied from Swagger
/// works against the Development database (Anna Petrosyan, the first copy of the first book).
/// </summary>
public sealed class BorrowingExamples : ISchemaFilter
{
    private static readonly Guid Anna = StaffSeedIds.StaffMembers.AnnaPetrosyan;
    private static readonly Guid Loan = BorrowingSeedIds.Loan(1);

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        OpenApiObject? example = context.Type switch
        {
            Type type when type == typeof(IssueLoanRequest) => new OpenApiObject
            {
                ["staffMemberId"] = Id(Anna),
                ["barcode"] = new OpenApiString(CatalogSeedIds.CopyBarcode(1, 1)),
            },
            Type type when type == typeof(ReturnLoanRequest) => new OpenApiObject
            {
                ["damaged"] = new OpenApiBoolean(false),
                ["note"] = new OpenApiString("Cover slightly worn, no damage."),
            },
            Type type when type == typeof(CreateReservationRequest) => new OpenApiObject
            {
                ["bookId"] = Id(CatalogSeedIds.Book(1)),
                ["staffMemberId"] = Id(Anna),
            },
            Type type when type == typeof(WaivePenaltyRequest) => new OpenApiObject
            {
                ["reason"] = new OpenApiString("The library desk was closed on the due date."),
            },
            Type type when type == typeof(LoanDto) => new OpenApiObject
            {
                ["loanId"] = Id(Loan),
                ["bookCopyId"] = Id(CatalogSeedIds.Copy(1, 1)),
                ["staffMemberId"] = Id(Anna),
                ["issuedAtUtc"] = Utc("2026-10-20T06:30:00Z"),
                ["dueAtUtc"] = Utc("2026-11-10T06:30:00Z"),
                ["renewalCount"] = new OpenApiInteger(0),
                ["status"] = new OpenApiString("Active"),
            },
            Type type when type == typeof(ReturnLoanResponse) => new OpenApiObject
            {
                ["loanId"] = Id(Loan),
                ["returnedAtUtc"] = Utc("2026-11-13T07:15:00Z"),
                ["status"] = new OpenApiString("Returned"),
                ["overdueWorkingDays"] = new OpenApiInteger(3),
                ["penaltyPointsApplied"] = new OpenApiInteger(3),
            },
            Type type when type == typeof(DeclareLostResponse) => new OpenApiObject
            {
                ["loanId"] = Id(Loan),
                ["declaredAtUtc"] = Utc("2026-11-20T08:00:00Z"),
                ["status"] = new OpenApiString("Lost"),
                ["penaltyPointsApplied"] = new OpenApiInteger(10),
            },
            Type type when type == typeof(LoanHistoryItemDto) => new OpenApiObject
            {
                ["loanId"] = Id(Loan),
                ["bookCopyId"] = Id(CatalogSeedIds.Copy(1, 1)),
                ["staffMemberId"] = Id(Anna),
                ["issuedAtUtc"] = Utc("2026-09-01T06:30:00Z"),
                ["dueAtUtc"] = Utc("2026-09-22T06:30:00Z"),
                ["returnedAtUtc"] = Utc("2026-09-25T07:15:00Z"),
                ["renewalCount"] = new OpenApiInteger(1),
                ["status"] = new OpenApiString("Returned"),
            },
            Type type when type == typeof(ReservationDto) => new OpenApiObject
            {
                ["reservationId"] = Id(BorrowingSeedIds.Reservation(1)),
                ["bookId"] = Id(CatalogSeedIds.Book(1)),
                ["staffMemberId"] = Id(Anna),
                ["createdAtUtc"] = Utc("2026-10-20T06:30:00Z"),
                ["status"] = new OpenApiString("Waiting"),
                ["queuePosition"] = new OpenApiInteger(2),
                ["copyId"] = new OpenApiNull(),
                ["readyUntilUtc"] = new OpenApiNull(),
            },
            Type type when type == typeof(PenaltyDto) => new OpenApiObject
            {
                ["penaltyId"] = Id(BorrowingSeedIds.Penalty(1)),
                ["staffMemberId"] = Id(Anna),
                ["loanId"] = Id(Loan),
                ["points"] = new OpenApiInteger(3),
                ["reason"] = new OpenApiString("Late return: 3 overdue working day(s)."),
                ["status"] = new OpenApiString("Active"),
                ["createdAtUtc"] = Utc("2026-11-13T07:15:00Z"),
                ["expiresAtUtc"] = Utc("2027-02-11T07:15:00Z"),
                ["waivedReason"] = new OpenApiNull(),
            },
            _ => null,
        };

        if (example is not null)
            schema.Example = example;
    }

    private static OpenApiString Id(Guid id) => new(id.ToString());

    private static OpenApiString Utc(string value) => new(value);
}
