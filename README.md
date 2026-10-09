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
   The first build takes a few minutes. The API waits until SQL Server is healthy, applies the migrations and, in Development, the seed data. `docker compose ps` shows both services as `healthy` when the system is ready.
3. Open (port `API_PORT` from `.env`, 8080 by default):
   - Swagger: http://localhost:8080/swagger (log in with `POST /api/auth/login`, then "Authorize" with the `accessToken`)
   - Health: http://localhost:8080/health/ready (`Healthy` when the database answers)
   - SQL Server: `localhost,1433`, user `sa`, the password from `.env`, database `LibraryApp`

Stop: `Ctrl+C` or `docker compose down`. Start with an empty database: `docker compose down -v`, then `docker compose up --build`.

### Development without the API container

1. Start only the database: `docker compose up -d sqlserver`. It starts again by itself with Docker Desktop (until you stop it with `docker compose stop`).
2. Run `LibraryApp.Api` from the IDE (profile `http`, http://localhost:5055/swagger). In Development the API reads `.env`: `ConnectionStrings__LibraryDb` points it to the SQL Server in Docker. Without that line it uses LocalDB from `appsettings.Development.json`.
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
