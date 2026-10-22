# LibraryApp — Corporate Library System

ASP.NET Core Web API (.NET 8), EF Core 8, SQL Server.

## Architecture

One layered monolith, one database, one repository. Requests flow top-down; Domain references nothing and only Infrastructure talks to the database or the mail server.

| Project | Holds |
|---|---|
| `LibraryApp.Api` | Controllers (`Controllers/<Module>/`), authentication, error handling, Swagger, `Program.cs` |
| `LibraryApp.Application` | Use cases (`<Module>/Services`), DTOs, validators, options; contracts between modules in `Abstractions/` |
| `LibraryApp.Domain` | Entities, business rules, domain events |
| `LibraryApp.Infrastructure` | EF Core (`LibraryDbContext`, configurations, migrations, repositories, seed), mail, event bus, daily jobs |

A module is a vertical slice through the four projects (the same module folder in each). Modules talk only through the interfaces in `Application/Abstractions/<Provider>` and through events (`IEventBus`, transactional outbox); they never write to another module's tables. Swagger (`/swagger`, Development) shows every endpoint under "All modules" and one definition per module in "Select a definition". Traceability of every requirement: [docs/traceability.md](docs/traceability.md).

| Module | Owner | Provides to other modules |
|---|---|---|
| Catalog | Group 1 | `ICatalogService` |
| Staff Members | Group 2 | `IStaffMemberService`, login and roles |
| Borrowing & Return | Group 3 | `IBorrowingReadService`, `ILoanQueryService`, `IReservationQueueService`; loan, reservation and penalty events |
| Notifications & Reporting | Group 4 | No interface; consumes the INT-07 events. Endpoints `/api/notifications` (in-app), `/api/reports` |
| Platform | Integration Owner | `IEventBus`, `IUnitOfWork`, `IClock`, `IWorkingDayCalendar`, `IApplicationLock`, `IDailyJob`, error handling, audit log, migrations, CI |

## Run it

Requirements: Docker Desktop (running).

1. Copy `.env.sample` to `.env` and set:
   - `MSSQL_SA_PASSWORD`: 8+ characters, 3 of: upper case, lower case, digit, symbol. It goes into connection strings, so do not use `;` `$` `"` `'` or spaces (`;` splits the connection string, `$` is replaced by docker compose);
   - `JWT_SIGNING_KEY`: 32+ random bytes, for example the output of `openssl rand -base64 32`.

   `.env` is ignored by git; never commit it.
2. Start everything:
   ```bash
   docker compose up --build
   ```
   The first build takes a few minutes. The API waits until SQL Server and smtp4dev are healthy, applies the migrations and, in Development, the seed data. `docker compose ps` shows all three services as `healthy` when the system is ready.
3. Open (port `API_PORT` from `.env`, 8080 by default):
   - Swagger: http://localhost:8080/swagger (log in with `POST /api/auth/login`, then "Authorize" with the `accessToken`)
   - Health: http://localhost:8080/health/ready (`Healthy` when the database answers)
   - SQL Server: `localhost,1433`, user `sa`, the password from `.env`, database `LibraryApp`
   - smtp4dev: http://localhost:5080 (port `SMTP4DEV_PORT`): every email the API sends appears here; nothing is delivered outside the machine

Stop: `Ctrl+C` or `docker compose down`. Start with an empty database: `docker compose down -v`, then `docker compose up --build`.

### Development without the API container

1. Start only the database and the mail server: `docker compose up -d sqlserver smtp4dev`. They start again by themselves with Docker Desktop (until you stop them with `docker compose stop`).
2. Run `LibraryApp.Api` from the IDE (profile `http`, http://localhost:5055/swagger). In Development the API reads `.env`: `ConnectionStrings__LibraryDb` points it to the SQL Server in Docker. Without that line it uses LocalDB from `appsettings.Development.json`. Emails go to smtp4dev on `localhost:2525` (port `SMTP4DEV_SMTP_PORT`).
3. Tests: see [Tests](#tests).
4. `dotnet ef database update` uses `LIBRARYAPP_DESIGNTIME_DB` from `.env`.

| Problem | Cause | Fix |
|---|---|---|
| `failed to connect to the docker API` | Docker Desktop is not running | Start Docker Desktop |
| `Set MSSQL_SA_PASSWORD in .env` or `Set JWT_SIGNING_KEY in .env` | No `.env` or an empty value | Step 1 |
| `sqlserver` stays unhealthy | Password does not meet the SQL Server rules | Change it in `.env`, then `docker compose down -v` |
| `sqlserver` exits or restarts on a Mac with Apple Silicon | SQL Server runs under emulation (Rosetta) and needs memory | Docker Desktop → Settings → Resources: 4 GB of memory or more |
| `api` exits or stays unhealthy | Migration failed or the database is not reachable | `docker compose logs api` |
| Port 1433 is in use | Local SQL Server or another app | Set `MSSQL_PORT` in `.env`, or stop the other app |
| Port 8080 is in use | Another app | Set `API_PORT` in `.env`, or stop the other app |
| Port 5080 or 2525 is in use | Another app | Set `SMTP4DEV_PORT` or `SMTP4DEV_SMTP_PORT` in `.env` (the API run from the IDE follows `SMTP4DEV_SMTP_PORT` through `Mail__Port` in `.env`), or stop the other app |

## Business-rule settings

Rule values and decided rule variants are settings in `appsettings.json`, one section per module, never constants in code. Each section is checked at start: a missing or inconsistent value stops the API with a message naming the key. Override a value per environment like any setting (for example `Borrowing__Penalties__LifetimeDays=120`). A change takes effect after a restart.

### Staff (`Staff`)

| Key | Value | Meaning | Requirement |
|---|---|---|---|
| `Import:MaxRows` | 200 | Maximum non-empty staff data rows, excluding the CSV header; positive integer | MEM-09 |
| `Import:MaxFileBytes` | 1000000 | Maximum CSV file size in bytes, from 1 to 2147483647 (in-memory parser bound) | MEM-09 |
| `RestrictedLimits:MaxLoans` | 1 | Loan limit for Restricted members; positive integer | BR-MEM-3 |

All three values are required. Missing or invalid settings prevent startup. Restricted members cannot renew (BR-MEM-3, BR-BOR-1); their renewal limit is fixed at `0` in code, not configured. Staff-category limits remain in the administrator-managed database.

CSV file limits are enforced in bytes and displayed as readable decimal sizes (for example, `1000000` as `1 MB` and `500000` as `500 KB`). HTTP request and multipart limits include an additional 100000 bytes for the multipart envelope. When increasing `Import:MaxFileBytes`, also check the reverse proxy's upload limit.

### Access tokens (`Jwt`)

| Key | Value | Meaning | Requirement |
|---|---|---|---|
| `AccessTokenMinutes` | 60 | Lifetime of an access token | MEM-02 |
| `SecurityStampCacheSeconds` | 30 | Account-state cache duration, from 1 to 30 seconds; defaults to 30 when omitted, validated at startup | MEM-02, NFR-02, S1-03-ADD-02 |

JWTs carry the account's `security_stamp`. Bearer authentication checks it against the current account and rejects a missing account, missing Staff profile, a mismatched stamp or Deactivated status with the standard 401 problem response. Restricted and Suspended members can still sign in to view their standing.

Role membership changes through `UserManager` rotate the stamp in the same Identity save through `StaffUserStore`. Email changes save the new stamp with the updated email in the Staff transaction; deactivation and password changes also rotate it.

Account state is cached per user and token stamp, with absolute expiration on each application instance. Email, password, role changes and deactivation invalidate previously issued tokens within the configured cache duration; requests do not extend it. A new login uses its new stamp immediately. Deactivated members cannot sign in. Tokens issued before this stamp check was added require a new login. Browser cookies continue to use `Ui:SecurityStampValidationInterval` (see [Browser UI](#browser-ui)).

### Borrowing (`Borrowing`)

| Key | Value | Meaning | Requirement |
|---|---|---|---|
| `Penalties:PointsPerLateWorkingDay` | 1 | Points per working day a loan is returned late | BOR-05 |
| `Penalties:MaxLatePoints` | 10 | Cap on the late points of one return | BOR-05 |
| `Penalties:DamagedPoints` | 5 | Points for a copy returned damaged | BOR-05, BOR-09 |
| `Penalties:LostPoints` | 10 | Points for a lost copy | BOR-05, BOR-09 |
| `Penalties:LifetimeDays` | 90 | Calendar days a penalty record counts | BR-BOR-6 |
| `Penalties:RestrictedFromPoints` | 5 | Active points from which the employee is Restricted | BR-BOR-6 |
| `Penalties:SuspendedFromPoints` | 10 | Active points from which the employee is Suspended | BR-BOR-6 |
| `Penalties:SuspensionDays` | 30 | Calendar days a suspension lasts once the points cross the Suspended threshold | BR-BOR-6, BQ-BA-01 |
| `Penalties:LateAndDamaged` | Sum | Late and damaged on one return: `Sum` — two records, points add up; `Highest` — one record with the higher points | BQ-BA-02 |
| `Loans:OverdueFrom` | NextLocalDay | When a loan counts as late: `NextLocalDay` — from the day after the local due date; `DueTime` — from the exact due time | BOR-05, BQ-BA-05 |
| `Loans:SuspendedMayRenew` | false | A Suspended employee may renew a loan; a Restricted one never may | BR-BOR-1, BQ-BA-11 |
| `Loans:AllowLostBeforeDue` | true | An Active loan may be declared lost before it is overdue; false: only a loan past the overdue rule (`Loans:OverdueFrom`) may be declared lost | BOR-09, BQ-BA-06 |
| `Reservations:MaxActive` | 3 | Active reservations per employee | BR-BOR-2 |
| `Reservations:PickupWorkingDays` | 3 | Working days a copy is held for the first person in the queue | BOR-07 |
| `Reservations:PickupCountsReleaseDay` | false | The day the copy is held counts as the first pickup day | BOR-07 |
| `Reservations:RestrictedMayReserve` | false | A Restricted employee may place reservations | BR-MEM-3 |
| `Reservations:SuspendedMayReserve` | false | A Suspended employee may place reservations | BR-BOR-6 |
| `Reservations:AnyCopyFulfils` | true | Issuing any copy of a book closes the employee's active reservation for it: a ready one is fulfilled and its held copy goes to the next in the queue, a waiting one is cancelled. false: only collecting the held copy fulfils | BOR-07 (BQ-BA-08) |

Point values are at most 10 per record (database constraint). The Development seed gives its staff members the statuses that match these values.

### Reporting (`Reporting`)

Configure reporting in `src/LibraryApp.Api/appsettings.json`.

| Key | Value | Meaning | Requirement |
|---|---|---|---|
| `DashboardCacheMinutes` | 5 | Dashboard cache lifetime in minutes (1–60) | REP-02 / REP-F4 |
| `TopBooksLimit` | 10 | Maximum top books returned (1–100) | REP-03 |
| `TopCategoriesLimit` | 5 | Maximum top categories returned (1–100) | REP-03 |
| `PurchaseCandidatesLimit` | 10 | Maximum purchase candidates returned (1–100) | REP-07 |

All four settings are required. Missing or out-of-range values prevent application startup.

### Notifications (`Notifications`)

| Key | Value | Meaning | Requirement |
|---|---|---|---|
| `Delivery:PollInterval` | 00:00:05 | Wait before the next round when no Pending email is left | NOT-03 |
| `Delivery:BatchSize` | 20 | Emails sent in one round | NOT-03 |
| `Delivery:Lease` | 00:15:00 | How long a claimed email is reserved for one API instance; must be longer than sending one batch | NOT-03 |
| `Delivery:RetryDelays` | 00:05:00, 00:15:00, 01:00:00 | Wait before each retry of a Failed email; the number of values is the number of retries (1 to 10, each 1 minute to 1 day) | NOT-08 |
| `Reminders:DueSoonWorkingDays` | 2 | A loan due within this many working days gets its DueSoonReminder (1 to 10) | NOT-05 |
| `Reminders:OverdueEveryWorkingDays` | 5 | An overdue loan gets an OverdueNotice every this many working days overdue (1 to 30) | NOT-05 |
| `Reminders:LineManagerCcAfterWorkingDays` | 14 | The line manager is in CC once the loan is overdue for more than this many working days (1 to 60) | NOT-05 |

Delivery runs only when `Jobs:Enabled` is true (see Daily jobs).

## Daily jobs

A module job that must run once per local (Asia/Yerevan) day — for example the Borrowing daily job (BOR-06) or the 09:00 reminders (NOT-05) — implements `IDailyJob` (`Application/Abstractions/Common`) and is registered in the module's `Add<Module>Module`:

```csharp
services.AddDailyJob<BorrowingDailyJob>(BorrowingDailyJob.Name);   // the name is stored with every run: never rename it
```

and gets a schedule in `appsettings.json` (a registered job without one stops the API at start):

```json
"Jobs": {
  "Schedules": {
    "borrowing-daily": { "Enabled": true, "RunAt": "00:30", "WorkingDaysOnly": false }
  }
}
```

The platform runs it at `RunAt`, once per day across all API instances (table `JobRuns`, one row per job and local date), catches up when an instance was down at that time, and retries a failed run after `Jobs:RetryDelay` up to `Jobs:MaxAttemptsPerDay` attempts; a failure on the last attempt is logged as Critical. `DailyJobRun.LastSuccessfulStartUtc` tells the job since when it has work to do after downtime. The job decides its own transactions (`IUnitOfWork`). `Jobs:Enabled=false` stops every job of an instance; the API test host sets it.

While the job works, its instance extends the lease of the run every third of `Jobs:Lease`, so a long run is never started a second time by another instance. Only an instance that stops in the middle of a run (or loses the database for longer than the lease) gives the run to another instance, which then does it again: write the job so that a second run of the same day changes nothing and sends nothing twice (NOT-06). A finished run whose result cannot be saved at that moment is not run again; the result is saved at the next check.

| Key | Value | Meaning |
|---|---|---|
| `Jobs:Enabled` | true | Master switch of the instance |
| `Jobs:PollInterval` | 00:01:00 | How often an instance checks whether a run is due |
| `Jobs:Lease` | 01:00:00 | Extended while the job works; when it runs out (the instance stopped), another instance takes the run over |
| `Jobs:RetryDelay` | 00:15:00 | Wait after a failed run |
| `Jobs:MaxAttemptsPerDay` | 5 | Attempts per job and day |

Scheduled jobs:

| Job (`Jobs:Schedules:<name>`) | RunAt | Working days only | What it does | Requirement |
|---|---|---|---|---|
| `Borrowing` | 00:30 | no | Active loans past the overdue rule (`Borrowing:Loans:OverdueFrom`) → Overdue with `LoanOverdue`; penalty records past 90 days → Expired; staff status recalculated: Active below `RestrictedFromPoints`, Suspended for `SuspensionDays` from the record that made `SuspendedFromPoints`, then Restricted while the points stay at or above `RestrictedFromPoints`; ReadyForPickup reservations past `ReadyUntilUtc` (the end of the last pickup working day) → Expired, the held copy goes to the next Waiting reservation with `ReservationReady` or becomes Available; no message about the expiry itself; Waiting reservations of a book deleted in Catalog → Cancelled; Waiting and ReadyForPickup reservations of a Deactivated staff member → Cancelled, a held copy goes to the next Waiting reservation or becomes Available | BOR-06, BOR-11, policy table, BQ-BA-01, BQ-BA-12, BQ-BA-13, BQ-G1-09, BQ-G2-03, BR-MEM-4 |
| `notifications-due-reminders` | 09:00 | yes | Open loans due within `Notifications:Reminders:DueSoonWorkingDays` working days (a due date on a day off is reminded with the next working day) → DueSoonReminder, once per due date; loans that Borrowing has marked Overdue, every `OverdueEveryWorkingDays` working days overdue (5, 10, 15, ...) → OverdueNotice, line manager in CC after `LineManagerCcAfterWorkingDays`. A reminder is due until the loan has it, so a day the job did not run is caught up at the next run (one reminder for the latest step). Each is stored in-app and as a Pending email for the delivery job; a loan that already has the same type that day is skipped; a run with a failed reminder fails, so the runner tries it again. Loans of Deactivated staff are left out | NOT-05, NOT-06 |

## Tests

```bash
dotnet test
```

Integration tests need a SQL Server. Every test class creates its own throw-away database on it and deletes it at the end. The server is decided in one place for all test projects and CI, in this order:

1. the environment variable `LIBRARYAPP_TEST_DB` (CI sets it; also a shell or the test runner settings of the IDE);
2. `LIBRARYAPP_TEST_DB` in `.env` (local development with `docker compose up -d sqlserver`);
3. LocalDB, when it is set nowhere (Windows only).

Only `LIBRARYAPP_TEST_DB` is read from `.env`; the API host of the tests gets its own database and a random JWT key, never the values of `.env`.

### Tests fail all at once

When the server does not answer, the run stops early and every database test fails with the same message: the server, where the setting came from (environment, `.env` or LocalDB) and what to do. Usually:

1. Docker Desktop is not running, or the container is stopped: start Docker Desktop, then `docker compose up -d sqlserver`.
2. An old environment variable `LIBRARYAPP_TEST_DB` points to another server: it wins over `.env`; remove it (and restart the IDE, which reads environment variables at start).
3. No `.env` and LocalDB is not installed or not running: create `.env` from `.env.sample`.

Databases left by an interrupted run (for example after stopping the test process) are deleted by the next run after two hours.

### Writing tests that need a database

Get the database only through the shared helpers: `TestDatabase.NewConnectionString()` in the infrastructure tests, `ApiHostFactory` in the API tests. Do not write a server address or read `LIBRARYAPP_TEST_DB` in a test: the test `No_test_chooses_its_SQL_Server_on_its_own` fails if a test does.

## Performance benchmark

NFR-01: "Typical API calls respond in under 500 ms; reports in under 2 s on 10,000 loans." `benchmark/LibraryApp.Benchmark` is a console application, not a test, and not part of `LibraryApp.sln`: the solution build, `dotnet test`, CI and the Docker image do not build it. A change in `src/` can therefore break it unnoticed; before a measurement, `dotnet build benchmark/LibraryApp.Benchmark -c Release` shows whether it still compiles. It times the running API over HTTP (Kestrel, authentication, database and serialization included) and writes a report to `docs/performance/`.

| Command | What it does |
|---|---|
| `seed [--total 10000]` | Adds synthetic returned loans (built by `Loan.Issue` and `Loan.Return`, spread over the last year in the free time of each copy, so they never overlap another loan of the same copy, the loans already in the database included) until the database holds the target number of loans. A second run adds nothing. No audit rows and no events are written. At the end it counts in the database the added loans that overlap another loan of their copy; exit code 1 when there is one. |
| `clean` | Removes the loans added by `seed` (their ids start with `be7c0000-0000-0000-0000-`). |
| `run [--base-url http://localhost:5055] [--iterations 50] [--warmup 5]` | Signs in with the seeded accounts (Administrator, Librarian, Staff Member), calls 17 read endpoints of every module, the three reports included, one at a time, and writes the report. Exit code 0: every endpoint within its limit; 1: a limit exceeded or a call failed; 2: the run could not be done. |

The database is the one of `ConnectionStrings__LibraryDb` (environment or `.env`), or `--connection`. Only a local SQL Server and a local API are accepted unless `--allow-remote` is given. The seeded password of the test accounts is used unless `--password` or `LIBRARYAPP_BENCHMARK_PASSWORD` is set.

A run on a separate database keeps the development data unchanged:

```bash
docker compose up -d sqlserver
```
```bash
export ConnectionStrings__LibraryDb="Server=localhost,1433;Database=LibraryApp_Benchmark;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True"
```
```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/LibraryApp.Api -c Release --no-launch-profile --urls http://localhost:5155
```
In a second terminal with the same `ConnectionStrings__LibraryDb`, once `http://localhost:5155/health/ready` answers 200 (the API has created and seeded the database):
```bash
dotnet run --project benchmark/LibraryApp.Benchmark -c Release -- seed
```
```bash
dotnet run --project benchmark/LibraryApp.Benchmark -c Release -- run --base-url http://localhost:5155
```

**The report.** File name: `yyyy-MM-dd[_tag]_<commit>[-dirty].md` (Asia/Yerevan date; `-dirty` when the working tree has uncommitted changes, so the measured code is not exactly the commit). It holds the result per endpoint (min, p50, p95, max, mean, the first call before warm-up, errors), the manifest (commit, tag, branch, API address, OS, CPU, memory, .NET, SQL Server version, row counts of the database) and every measured time. An endpoint passes when every call succeeded and its 95th percentile is within its limit. The dashboard report is cached by the application (`Reporting:DashboardCacheMinutes`): its measured calls are cache hits, and its first call shows the uncached time.

**For comparable numbers:**
- run the API in Release (`-c Release`), with no debugger attached, and wait until `/health/ready` answers;
- keep the machine otherwise idle: no build, test run, IDE indexing or second API instance at the same time; on a laptop, connect the power supply;
- commit first, so the report names exactly the measured code (no `-dirty`);
- compare only reports made on the same machine: the manifest records which one; numbers of a GitHub runner or another computer are not comparable;
- one report is one sample: when a result is close to a limit, repeat the run.

## Demo data

In Development the API seeds synthetic data (C-2) at start: 57 books with 115 copies, 30 staff members in 6 departments, and 40 loans of which 6 are overdue (the dashboard shows an overdue rate of 15.0 %), with a reservation queue, a copy held for pickup and penalty records in every status. Loan dates are counted from the moment of seeding: the figures stay exact for 12 days, after which more loans become overdue.

The seed only fills an empty database; it never changes existing data. Before a demo, or after a change of the seed, recreate the Development database:

- with Docker: `docker compose down -v`, then `docker compose up --build`;
- with the API run from the IDE: stop the API, run `dotnet ef database drop --force --project src/LibraryApp.Infrastructure` (the database of `LIBRARYAPP_DESIGNTIME_DB` in `.env`), then start the API: it applies the migrations and the seed.

### Catalog covers (`Catalog:Covers`)

`Catalog:Covers:BaseUrl` is the address of the cover storage. The database stores only relative paths such as
`library-covers/01.png`; the API combines each path with this setting before returning `CoverImageUrl`. The value may be
empty to turn covers off. Otherwise it must be an absolute HTTPS address ending in `/`, without userinfo, query,
fragment or unsafe characters. Covers need internet access from the browser; when they are unavailable, the coloured
text cover remains visible. To move to another storage, copy the files to the new location and change this one setting;
the database, API code and contracts do not change.

The seed fills only an empty database, so a Development database created before the covers keeps its books without
cover paths. Recreate it once, as described in [Demo data](#demo-data), to get the covers.

## Browser UI

Razor Pages live in the API host (UI guide in the team docs): the shared shell in `src/LibraryApp.Api/Pages` and `Ui/`, each group's pages in `src/LibraryApp.Api/Areas/<Module>`.

- **Two ways to sign in, chosen by path.** `/api/*` accepts only the JWT; every other path accepts only the UI cookie. The API never accepts the cookie (no cross-site requests), and pages never accept the JWT.
- **Every page needs a signed-in user** unless its PageModel is `[AllowAnonymous]` (only sign-in, sign-out and the status pages). Library staff pages add `[Authorize(Policy = UiPolicies.LibraryStaff)]`.
- **Errors.** `/api` answers ProblemDetails; pages show the Error, Not found and Forbidden pages. In a form, catch `DomainException` with `this.DomainError(ex)`; validators run before the handler and fill `ModelState`.
- **Session.** The cookie ends with the access token (`Jwt:AccessTokenMinutes`) and is checked against the account's security stamp every `Ui:SecurityStampValidationInterval` (default 5 minutes): a password change or a deactivation ends it.
- **Known limitation.** An access token (JWT) stays valid until it expires, even after a password change or a deactivation; token revocation is not in the requirements.
- **Styles.** `wwwroot/css/site.css` is the UI demo's `design/styles.css` plus the fonts and the shell's additions; it is a shared file (changes by pull request with Integration Owner review). The pages send a Content-Security-Policy without inline scripts or styles, so a `style=""` copied from the demo has no effect: use the spacing helpers at the end of `site.css` instead (`style="margin-bottom: 16px"` → `class="mb-16"`, `gap: 8px` → `gap-8`, `min-width: 160px` → `min-w-160`, the padded button row under a table → `card-foot`).
- **Page tests** sign a seeded user in with `UiTestClient.SignedInAsync(factory, email, password)` (`tests/LibraryApp.Api.IntegrationTests/Ui`).

Staff pages (MEM-01, MEM-07): `/staff` provides search, department/status filters and pagination; `/staff/{id}` shows the profile and deactivation form. Both routes require a Librarian or Library Administrator.

The profile invokes Group 3's `StaffBorrowingSummary` View Component with `staffMemberId` when available; otherwise it displays an unavailable message. Borrowing actions remain owned by Group 3.

## Test accounts

Synthetic demo data (C-2), created by the seed in Development only. Every account has the password `Library2026`.

| E-mail | Role | Status |
|---|---|---|
| `lilit.grigoryan@ameriabank.am` | LibraryAdministrator | Active |
| `aram.sargsyan@ameriabank.am` | Librarian | Active |
| `anna.petrosyan@ameriabank.am` | StaffMember | Active |
| `davit.hakobyan@ameriabank.am` | StaffMember | Active |
| `mariam.avetisyan@ameriabank.am` | StaffMember | Active |
| `gor.mkrtchyan@ameriabank.am` | StaffMember | Restricted |
| `sona.vardanyan@ameriabank.am` | StaffMember | Suspended |
| `hayk.manukyan@ameriabank.am` | StaffMember | Deactivated |
