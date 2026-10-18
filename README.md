# LibraryApp — Corporate Library System

ASP.NET Core Web API (.NET 8), EF Core 8, SQL Server.

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
| `Reservations:MaxActive` | 3 | Active reservations per employee | BR-BOR-2 |
| `Reservations:PickupWorkingDays` | 3 | Working days a copy is held for the first person in the queue | BOR-07 |
| `Reservations:PickupCountsReleaseDay` | false | The day the copy is held counts as the first pickup day | BOR-07 |
| `Reservations:RestrictedMayReserve` | false | A Restricted employee may place reservations | BR-MEM-3 |
| `Reservations:SuspendedMayReserve` | false | A Suspended employee may place reservations | BR-BOR-6 |

Point values are at most 10 per record (database constraint). The Development seed gives its staff members the statuses that match these values.

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
## Browser UI

Razor Pages live in the API host (UI guide in the team docs): the shared shell in `src/LibraryApp.Api/Pages` and `Ui/`, each group's pages in `src/LibraryApp.Api/Areas/<Module>`.

- **Two ways to sign in, chosen by path.** `/api/*` accepts only the JWT; every other path accepts only the UI cookie. The API never accepts the cookie (no cross-site requests), and pages never accept the JWT.
- **Every page needs a signed-in user** unless its PageModel is `[AllowAnonymous]` (only sign-in, sign-out and the status pages). Library staff pages add `[Authorize(Policy = UiPolicies.LibraryStaff)]`.
- **Errors.** `/api` answers ProblemDetails; pages show the Error, Not found and Forbidden pages. In a form, catch `DomainException` with `this.DomainError(ex)`; validators run before the handler and fill `ModelState`.
- **Session.** The cookie ends with the access token (`Jwt:AccessTokenMinutes`) and is checked against the account's security stamp every `Ui:SecurityStampValidationInterval` (default 5 minutes): a password change or a deactivation ends it.
- **Known limitation.** An access token (JWT) stays valid until it expires, even after a password change or a deactivation; token revocation is not in the requirements.
- **Styles.** `wwwroot/css/site.css` is the UI demo's `design/styles.css` plus the fonts and the shell's additions; it is a shared file (changes by pull request with Integration Owner review). The pages send a Content-Security-Policy without inline scripts or styles, so a `style=""` copied from the demo has no effect: use the spacing helpers at the end of `site.css` instead (`style="margin-bottom: 16px"` → `class="mb-16"`, `gap: 8px` → `gap-8`, `min-width: 160px` → `min-w-160`, the padded button row under a table → `card-foot`).
- **Page tests** sign a seeded user in with `UiTestClient.SignedInAsync(factory, email, password)` (`tests/LibraryApp.Api.IntegrationTests/Ui`).

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
