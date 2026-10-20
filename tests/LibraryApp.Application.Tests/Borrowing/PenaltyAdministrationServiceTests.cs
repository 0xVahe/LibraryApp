using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Common;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Borrowing;

/// <summary>G3-08 (BOR-08): the waiver under the owner's staff lock, the status recalculated in the same transaction.</summary>
public sealed class PenaltyAdministrationServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private readonly Guid _staffId = Guid.NewGuid();
    private readonly Records _records = new();
    private readonly RecordingPenaltyComponent _component = new();
    private readonly FakeApplicationLock _locks = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    private PenaltyAdministrationService Service() =>
        new(_records, _component, _unitOfWork, _locks, new TestClock(Now));

    private PenaltyRecord Seed(int points = 5)
    {
        PenaltyRecord record = PenaltyRecord.Create(
            Guid.NewGuid(), _staffId, Guid.NewGuid(), points, "Returned late", Now.AddDays(-1), Now.AddDays(89));
        _records.All.Add(record);
        return record;
    }

    [Fact]
    public async Task A_waiver_waives_the_record_and_recalculates_the_owners_status_under_the_staff_lock()
    {
        PenaltyRecord record = Seed();
        _component.Watched = record;

        PenaltyDto result = await Service().WaiveAsync(record.Id, new WaivePenaltyRequest("  Courtesy  "));

        Assert.Equal((PenaltyRecordStatus.Waived, "Courtesy"), (result.Status, result.WaivedReason));
        Assert.Equal(new[] { $"lock:{BorrowingLocks.Staff(_staffId)}" }, _locks.Log);

        // The recalculation runs after the record is waived, inside the same transaction.
        Assert.Equal((_staffId, Now, PenaltyRecordStatus.Waived), Assert.Single(_component.Calls));
        Assert.Equal(1, _unitOfWork.Transactions);
    }

    [Fact]
    public async Task An_unknown_record_is_not_found_before_any_lock()
    {
        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service().WaiveAsync(Guid.NewGuid(), new WaivePenaltyRequest("Reason")));

        Assert.Equal(BorrowingErrors.PenaltyNotFound, error.Error);
        Assert.Empty(_locks.Log);
        Assert.Empty(_component.Calls);
    }

    [Fact]
    public async Task A_record_that_is_not_active_is_refused_without_a_recalculation()
    {
        PenaltyRecord record = Seed();
        record.Waive("Earlier");

        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service().WaiveAsync(record.Id, new WaivePenaltyRequest("Again")));

        Assert.Equal(BorrowingErrors.PenaltyNotActive, error.Error);
        Assert.Equal("Earlier", record.WaivedReason);
        Assert.Empty(_component.Calls);
    }

    private sealed class Records : IPenaltyRepository
    {
        public List<PenaltyRecord> All { get; } = [];

        public Task<PenaltyRecord?> FindAsync(Guid penaltyId, CancellationToken ct = default) =>
            Task.FromResult(All.SingleOrDefault(r => r.Id == penaltyId));

        public Task<PenaltyRecord?> GetForUpdateAsync(Guid penaltyId, CancellationToken ct = default) =>
            Task.FromResult(All.SingleOrDefault(r => r.Id == penaltyId));

        public Task<IReadOnlyList<PenaltyRecord>> GetForStaffAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<PenaltyRecord>> PageForStaffAsync(Guid staffMemberId, PenaltyRecordStatus? status, PageRequest page, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(PenaltyRecord penalty, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Records each recalculation with the status the waived record had at that moment.</summary>
    private sealed class RecordingPenaltyComponent : IPenaltyService
    {
        private readonly List<(Guid, DateTime, PenaltyRecordStatus)> _calls = [];
        public IReadOnlyList<(Guid, DateTime, PenaltyRecordStatus)> Calls => _calls;
        public PenaltyRecord? Watched { get; set; }

        public Task<PenaltyRecalculationResult> RecalculateStatusAsync(Guid staffMemberId, DateTime nowUtc, CancellationToken ct = default)
        {
            _calls.Add((staffMemberId, nowUtc, Watched?.Status ?? PenaltyRecordStatus.Active));
            return Task.FromResult(new PenaltyRecalculationResult(0, 0, StatusChanged: true));
        }

        public Task<PenaltyApplicationResult> ApplyAsync(Loan loan, IReadOnlyList<ReturnPenaltyItem> items, DateTime nowUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Transactions { get; private set; }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            Transactions++;
            return await operation(ct);
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
            ExecuteInTransactionAsync(async token => { await operation(token); return 0; }, ct);
    }
}
