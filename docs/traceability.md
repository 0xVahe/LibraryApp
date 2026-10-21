# Traceability matrix (D7)

Requirement ID → where it is implemented (class, endpoint, setting) → the tests that prove it. One section per owner; each group fills its own section in its own pull request, keeping the format. Paths are relative to `src/` and `tests/`.

Status: ✅ implemented and tested · 🟡 implemented, test pending · ⏳ not implemented yet.

## Platform and shared requirements (Integration Owner)

| Requirement | Implementation | Tests | Status |
|---|---|---|---|
| INT-06 Event bus `IEventBus.PublishAsync` | `Application/Abstractions/Messaging/IEventBus`; `Infrastructure/Messaging/OutboxEventBus`, `OutboxDispatcher`, `InProcessEventBus` (`Messaging:Delivery`) | `Infrastructure.IntegrationTests/Messaging/OutboxTests` | ✅ |
| INT-07 Events carry ids, not entities | `Domain/Events/*` (8 records) | `Domain.Tests/Events/DomainEventRulesTests` | ✅ |
| SD-06…08 Delivered once per handler, retried, dead letters | `OutboxDispatcher`, `ProcessedEvents` inbox, `OutboxHealthCheck` | `OutboxTests` | ✅ |
| NFR-01 Typical calls under 500 ms, reports under 2 s on 10,000 loans | No dedicated code: the requirement is met by the modules' queries. Measured by `benchmark/LibraryApp.Benchmark` (outside `src/` and `tests/`, not run by CI): `seed` up to 10,000 loans, `run` times 17 read endpoints of every module and the three reports over HTTP (README, "Performance benchmark") | Benchmark reports in `docs/performance/` (per endpoint p50, p95, max, first call; manifest of commit, machine and database) | ✅ |
| NFR-02 JWT on every endpoint except login and health | `Program.cs` `MapControllers().RequireAuthorization()`, `[AllowAnonymous]` on login and health | module endpoint tests (401 without a token), `Auth/HttpCurrentUserTests` | ✅ |
| NFR-04 No secrets in git | `.env` (git-ignored), `.env.sample`; CI job `secret-scan` (gitleaks) | CI `secret-scan` | ✅ |
| NFR-07 Errors as ProblemDetails, no stack trace | `Api/ErrorHandling/GlobalExceptionHandler`, `HttpErrors`, `UseStatusCodePages` | `Api.IntegrationTests/ErrorHandlingTests`, `Application.Tests/Common/ErrorCatalogTests`, `CommonErrorsTests`, `Domain.Tests/Common/ErrorTests` | ✅ |
| NFR-08 FK, unique indexes, transactions | `Persistence/LibraryDbContext`, `Conventions/LibraryModelConventions`, `UnitOfWork`, `SqlServerApplicationLock` | `Persistence/ModelConventionsTests`, `UnitOfWorkTests`, `UnitOfWorkReferenceTests`, `ApplicationLockTests` | ✅ |
| NFR-09 Application coverage ≥ 50% | CI step "Coverage gate" (`APPLICATION_MIN_COVERAGE`, default 50) | CI `build-and-test` | ✅ |
| NFR-10 Request log: user, route, duration, status | `Program.cs` `UseSerilogRequestLogging` (`Api/Logging/RequestLogging`), Serilog per host (`preserveStaticLogger`) | `ErrorHandlingTests.The_request_log_survives_another_host_stopping`, `RequestLoggingTests`, `HostLoggingTests` | ✅ |
| NFR-11 Swagger groups endpoints by module, with XML comments and example payloads | `Program.cs` `AddSwaggerGen` (XML of Api, Application, Domain); `OpenApi/ModuleDocuments` — the complete `v1` and one document per module (folder of the controller); examples per module (`OpenApi/*Examples`) | `ApiHostTests` (swagger.json 200), `SwaggerModuleTests`; module Swagger tests (`CatalogSwaggerTests`, `BorrowingSwaggerTests`) | ✅ |
| NFR-12 ISO 8601 UTC, Asia/Yerevan | `IClock`, `Time/SystemClock` (`Time:TimeZoneId`), `Converters/UtcDateTimeConverters` | `Time/SystemClockTests`, `Persistence/UtcDateTimeConverterTests` | ✅ |
| NFR-13 One-command start | `docker-compose.yml`, `src/LibraryApp.Api/Dockerfile`, `/health/live`, `/health/ready` | `ApiHostTests` (health 200); manual `docker compose up --build` | ✅ |
| NFR-14 Audit of books, staff, loans, penalties | `Domain/Auditing/AuditLog`, `Persistence/Auditing/AuditSaveChangesInterceptor` | `Auditing/AuditLogTests`, `AuditLogEndpointTests` | ✅ |
| BR-BOR-5 One working-day calendar, holidays configurable | `Abstractions/Common/IWorkingDayCalendar`, `Time/WorkingDayCalendar` (`WorkingDayCalendar:Holidays`, checked at start) | `Time/WorkingDayCalendarTests`, `WorkingDayCalendarSettingsTests` | ✅ |
| SD-10 Daily jobs once per day across instances | `Abstractions/Common/IDailyJob`, `Jobs/DailyJobRunner`, `JobRunStore`, table `JobRuns` (`Jobs` settings) | `Jobs/DailyJobRunnerTests`, `Jobs/DailyJobRegistrationTests` | ✅ |
| SD-01 Application-generated sequential Guid | `Persistence/SequentialGuidGenerator` | `SequentialGuidGeneratorTests` | ✅ |
| SD-17 Development seed, idempotent | `Persistence/Seed/DatabaseInitializer`, `IModuleSeeder` | `DatabaseInitializerTests` | ✅ |
| NOT-02 Default templates in every environment | migration `AddDefaultNotificationTemplates` | `Notifications/NotificationTemplatesMigrationTests` | ✅ |
| X-09 Identity in the shared database, roles everywhere | `Identity/*`, migration `AddStaffRoles` | `Identity/IdentityModelTests`, `StaffRolesMigrationTests` | ✅ |
| SD-21 Rule values in settings, checked at start | module options (`Borrowing`, `Jobs`, …) with `ValidateOnStart` | module options tests (`BorrowingOptionsValidatorTests`, `DailyJobRegistrationTests`) | ✅ |

## Module sections (owners fill in)

Copy this table into the section of your module and add one row per requirement of your module (requirements package, section of the module).

| Requirement | Implementation | Tests | Status |
|---|---|---|---|
| `<ID> <short text>` | `<class / endpoint / setting>` | `<test class.method>` | ✅ / 🟡 / ⏳ |

### Module 1 — Catalog (Group 1)

### Module 2 — Staff Members (Group 2)

### Module 3 — Borrowing & Return (Group 3)

| Requirement | Implementation | Tests | Status |
|---|---|---|---|
| BOR-01 Issue by staff member and copy barcode; due = issue + category loan days | `POST /api/loans` → `Application/Borrowing/Services/LoanService.IssueAsync`; Loan desk `/desk` (`Areas/Borrowing/Pages/Desk`) | `Application.Tests/Borrowing/LoanServiceTests`, `Api.IntegrationTests/Borrowing/LoanEndpointTests`, `LoanDeskPageTests` | ✅ |
| BOR-02 Check `CanBorrowAsync` and copy availability, reject with the reason | `LoanService.IssueAsync` (`IStaffMemberService.CanBorrowAsync`, `ICatalogService.GetCopyByBarcodeAsync`, loan limit) | `LoanServiceTests`, `LoanEndpointTests` | ✅ |
| BOR-03 Return: ReturnedAt, copy Available or Reserved for the queue, close the loan | `POST /api/loans/{id}/return` → `LoanReturnService.ReturnAsync`; by barcode `LoanService.FindOpenByBarcodeAsync`; Loan desk "Take back" | `LoanReturnEndpointTests`, `LoanByBarcodeTests`, `Domain.Tests/Borrowing/LoanReturnTests`, `LoanDeskPageTests` | ✅ |
| BOR-04 Renew: due + loan days, RenewalCount + 1 | `POST /api/loans/{id}/renew` → `LoanRenewalService.RenewAsync`; My library "Renew" (`Pages/RenewLoan`) | `LoanRenewalServiceTests`, `LoanRenewalEndpointTests`, `BorrowingBlocksPageTests` | ✅ |
| BOR-05 Late points by the policy table, staff status updated | `ReturnPenaltyCalculator`, `PenaltyService.ApplyAsync`, `Domain/Borrowing/PenaltyPolicy` (`Borrowing:Penalties`) | `ReturnPenaltyCalculatorTests`, `PenaltyServiceTests`, `Domain.Tests/Borrowing/PenaltyPolicyTest`, `LoanReturnEndpointTests` | ✅ |
| BOR-06 Daily job 00:30: overdue loans, 90-day points, events | `Infrastructure/Jobs/Borrowing/BorrowingDailyJob` → `BorrowingDailyJobService` (`Jobs:Borrowing`) | `BorrowingDailyJobServiceTests`, `Infrastructure.IntegrationTests/Jobs/BorrowingDailyJobTests`, `BorrowingDailyJobDatabaseTests` | ✅ |
| BOR-07 FIFO queue per book; returned copy ReadyForPickup for 3 working days | `POST /api/reservations` → `ReservationService.CreateAsync`; `ReservationQueueService` (`IReservationQueueService.OfferCopyAsync` for Catalog); Book page `ReservePanel`; Loan desk "Waiting for pickup" (`IPickupListService`) | `ReservationServiceTests`, `ReservationQueueServiceTests`, `ReservationEndpointTests`, `ReservationQueueEndpointTests`, `PickupListServiceTests`, `ReservePanelPageTests`, `LoanDeskPageTests` | ✅ |
| BOR-08 Waive a penalty record with a mandatory reason | `POST /api/penalties/{id}/waive` → `PenaltyAdministrationService.WaiveAsync`; Staff member page "Standing" (`Pages/WaivePenalty`) | `PenaltyAdministrationServiceTests`, `PenaltyWaiveEndpointTests`, `WaivePenaltyPageTests` | ✅ |
| BOR-09 Declare lost or damaged with the policy points | lost: `POST /api/loans/{id}/lost` → `LoanLostService`; damaged: `ReturnLoanRequest.Damaged`; Loan desk and Staff member "Declare lost" (`Pages/DeclareLost`) | `LoanLostServiceTests`, `LoanLostEndpointTests`, `LoanReturnEndpointTests`, `DeclareLostPageTests` | ✅ |
| BOR-10 Loan history per staff member and per book, filters, paging | `GET /api/loans` → `LoanHistoryService`, `Infrastructure/.../LoanHistoryQuery`; page `/staff/{id}/loans` (`Pages/LoanHistory`) | `LoanHistoryEndpointTests`, `LoanHistoryPageTests`, `OverdueRuleTests` | ✅ |
| BOR-11 Expire uncollected reservations, next in queue | `BorrowingDailyJobService.ExpirePickupAsync` | `BorrowingDailyJobServiceTests`, `BorrowingDailyJobDatabaseTests` | ✅ |
| BOR-12 Staff member cancels own reservation | `POST /api/reservations/{id}/cancel` → `ReservationService.CancelAsync`; My library "Cancel" (`Pages/CancelReservation`) | `ReservationServiceTests`, `ReservationCancelEndpointTests`, `BorrowingBlocksPageTests` | ✅ |
| BR-BOR-1 No renewal when overdue, at max renewals, Restricted, or someone waits | `LoanRenewalService.RenewAsync` (order of the checks) | `LoanRenewalServiceTests`, `LoanRenewalEndpointTests` | ✅ |
| BR-BOR-2 No reservation of a held book; at most 3 active reservations | `ReservationService.CreateAsync` (`Borrowing:Reservations:MaxActive`) | `ReservationServiceTests`, `ReservationEndpointTests`, `CreateReservationRequestValidatorTests` | ✅ |
| BR-BOR-3 Reservation only when no copy is Available | `ReservationService.CreateAsync` (`borrowing.reservation_copy_available`); `ReservePanel` offers no button then | `ReservationServiceTests`, `ReservationEndpointTests`, `ReservePanelPageTests` | ✅ |
| BR-BOR-4 Issue, return, renew and penalty operations in one transaction | `IUnitOfWork.ExecuteInTransactionAsync` in `LoanService`, `LoanReturnService`, `LoanRenewalService`, `LoanLostService`, `PenaltyAdministrationService`; locks `BorrowingLocks` | `LoanEndpointTests` (rollback when the save fails), `LoanReturnEndpointTests` (two parallel returns) | ✅ |
| BR-BOR-5 Working days exclude weekends and configured Armenian holidays | `IWorkingDayCalendar` (`WorkingDayCalendar:Holidays`), used by `ReturnPenaltyCalculator` and the pickup deadline | `ReturnPenaltyCalculatorTests`, `WorkingDayCalendarSettingsTests`, `BorrowingSeedCalendarTests` | ✅ |
| BR-BOR-6 Events LoanIssued, LoanReturned, LoanRenewed, LoanOverdue, ReservationReady, PenaltyApplied, StaffStatusChanged | `IEventBus.PublishAsync` in the Borrowing services and the daily job; `Domain/Events/*` | `LoanServiceTests`, `LoanReturnEndpointTests`, `PenaltyServiceTests`, `BorrowingDailyJobServiceTests`, `ReservationQueueServiceTests` | ✅ |
| Penalty-point policy: late 1/working day max 10, lost 10, damaged 5, Restricted 5, Suspended 10 for 30 days, 90-day expiry | `Domain/Borrowing/PenaltyPolicy`, `PenaltyRules`; `Borrowing:Penalties` (`BorrowingOptionsValidator`) | `PenaltyPolicyTest`, `PenaltyServiceTests`, `BorrowingOptionsValidatorTests`, `BorrowingSettingsTests` | ✅ |
| NFR-03 A Staff Member reaches only their own loans, reservations and points | `ICurrentUser` checks in `LoanRenewalService`, `ReservationService`, `LoanHistoryService`; `StaffBorrowingReader.MaySee` for the blocks | `LoanHistoryEndpointTests`, `LoanRenewalEndpointTests`, `ReservationCancelEndpointTests`, `BorrowingBlocksPageTests` | ✅ |
| NFR-11 Every Borrowing endpoint documented in Swagger with responses and examples | `Controllers/Borrowing/*` XML comments, `Api/OpenApi/BorrowingExamples` | `BorrowingSwaggerTests` | ✅ |

### Module 4 — Notifications & Reporting (Group 4)
