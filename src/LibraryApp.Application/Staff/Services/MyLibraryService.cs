using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.Logging;

namespace LibraryApp.Application.Staff.Services;

/// <summary>MEM-08 "My library". Read only, no transaction.</summary>
public sealed class MyLibraryService(
    ICurrentUser currentUser,
    IStaffManagementRepository members,
    IUnitOfWork unitOfWork,
    IBorrowingReadService borrowing,
    ICatalogService catalog,
    IClock clock,
    ILogger<MyLibraryService> logger) : IMyLibraryService
{
    public async Task<MyLibraryDto> GetAsync(CancellationToken ct = default)
    {
        // NFR-03: the member comes from the access token only, never from the request.
        var memberId = currentUser.StaffMemberId ?? throw new DomainException(StaffErrors.NotFound);
        var member = await members.GetDetailsAsync(memberId, ct) ?? throw new DomainException(StaffErrors.NotFound);
        var summary = await borrowing.GetStaffSummaryAsync(memberId, ct);

        var barcodes = (await catalog.GetCopiesAsync(summary.ActiveLoans.Select(x => x.BookCopyId).Distinct().ToArray(), ct))
            .ToDictionary(x => x.Id, x => x.Barcode);
        var titles = await GetTitlesAsync(summary, ct);
        var now = clock.UtcNow;

        var loans = summary.ActiveLoans.Select(loan =>
        {
            if (!barcodes.TryGetValue(loan.BookCopyId, out var barcode))
                logger.LogWarning("Cannot resolve copy {BookCopyId} for loan {LoanId}", loan.BookCopyId, loan.LoanId);
            return new MyLoanDto(loan.LoanId, loan.BookId, titles[loan.BookId], barcode, loan.IssuedAtUtc,
                loan.DueAtUtc, loan.Status == LoanStatus.Overdue || loan.DueAtUtc < now, loan.RenewalCount);
        }).ToArray();

        var reservations = summary.Reservations.Select(x => new MyReservationDto(x.ReservationId, x.BookId,
            titles[x.BookId], x.Status.ToString(), x.QueuePosition, x.ReadyUntilUtc, x.CreatedAtUtc)).ToArray();

        var penalties = summary.ActivePenalties.Select(x => new MyPenaltyDto(x.PenaltyId, x.BookId,
            titles[x.BookId], x.Points, x.Reason, x.CreatedAtUtc, x.ExpiresAtUtc)).ToArray();

        var profile = new MyProfileDto(member.Id, member.FirstName, member.LastName, member.CorporateEmail,
            member.DepartmentName, member.Position, member.StaffCategoryName, member.Status);

        return new MyLibraryDto(profile, loans, reservations, summary.ActivePenaltyPoints, penalties);
    }

    public async Task<MyProfileDto> UpdateProfileAsync(UpdateMyProfileRequest request, CancellationToken ct = default)
    {
        var memberId = currentUser.StaffMemberId ?? throw new DomainException(StaffErrors.NotFound);

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var member = await members.GetByIdAsync(memberId, token)
                         ?? throw new DomainException(StaffErrors.NotFound);
            if (member.Status == StaffMemberStatus.Deactivated)
                throw new DomainException(StaffErrors.DeactivatedProfileLocked);

            var existing = await members.GetDetailsAsync(memberId, token)
                           ?? throw new DomainException(StaffErrors.NotFound);

            // Reuse the domain profile rule while retaining every HR-managed field as-is.
            member.UpdateProfile(request.FirstName, request.LastName, member.CorporateEmail, member.Position,
                member.LineManagerEmail);

            return new MyProfileDto(member.Id, member.FirstName, member.LastName, existing.CorporateEmail,
                existing.DepartmentName, existing.Position, existing.StaffCategoryName, existing.Status);
        }, ct);
    }

    /// <summary>One Catalog call per distinct book; a book Catalog cannot resolve gets a null title, not an error.</summary>
    private async Task<Dictionary<Guid, string?>> GetTitlesAsync(StaffBorrowingSummary summary, CancellationToken ct)
    {
        var bookIds = summary.ActiveLoans.Select(x => x.BookId)
            .Concat(summary.Reservations.Select(x => x.BookId))
            .Concat(summary.ActivePenalties.Select(x => x.BookId))
            .Distinct();

        var titles = new Dictionary<Guid, string?>();
        foreach (var bookId in bookIds)
        {
            var book = await catalog.GetBookAsync(bookId, ct);
            if (book is null)
                logger.LogWarning("Cannot resolve book {BookId} for My library", bookId);
            titles.Add(bookId, book?.Title);
        }
        return titles;
    }
}
