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

1. Start only the database: `docker compose up -d sqlserver`.
2. Run `LibraryApp.Api` from the IDE (profile `http`, http://localhost:5055/swagger). In Development the API reads `.env`: `ConnectionStrings__LibraryDb` points it to the SQL Server in Docker. Without that line it uses LocalDB from `appsettings.Development.json`.
3. Tests: every test class creates its own throw-away database on the server in `LIBRARYAPP_TEST_DB` and deletes it at the end. The infrastructure tests read it from `.env`; for the API tests set it as an environment variable (user variable or the test runner settings of the IDE). Without it the tests use LocalDB.
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
