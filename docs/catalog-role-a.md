# Role A local handoff

Branch: `feature/catalog-authors-categories` (unchanged). No commits, pushes, or GitHub changes.

## Scope and status

- S1-04, Catalog reference seed: the eight required root categories are implemented with stable IDs in `CatalogSeedData.Categories`. The seeder follows the existing Development seed runner and transaction. SQL repeatability tests compile but are blocked at database setup. No agreed author/publisher seed list was supplied, so no such data was invented.
- S1-05 / CAT-02: author CRUD and atomic replacement of an existing active book's authors are implemented. Empty assignments are rejected; duplicate IDs produce one link; missing authors leave existing links intact; referenced authors cannot be deleted, including references from soft-deleted books. Application tests pass. Publisher entity, mapping, DbSet, and migration already exist; publisher CRUD is not specified by the available requirements.
- S1-06 / CAT-03: category CRUD is implemented. Existing domain rules reject self-parenting, cycles, third levels, and reparenting a root with descendants. The repository loads children and parent before applying those rules. References from books (including deleted books) or children prevent deletion. Application tests pass.
- CAT-10: blocked on approved BookSuggestion fields/workflow and Integration Owner schema work, as identified in `Answers-Group-1.md`.
- Full ClickUp descriptions and the named prerequisite PR remain unavailable. No acceptance criteria beyond the supplied requirements and existing schema were assumed.

## Endpoints and integration

- `GET/POST /api/authors`, `GET/PUT/DELETE /api/authors/{id}`.
- `PUT /api/books/{bookId}/authors`, body `{"authorIds":["<guid>"]}`.
- `GET/POST /api/categories`, `GET/PUT/DELETE /api/categories/{id}`; a null `parentCategoryId` makes a root.
- Reads require authentication; writes require existing LibraryAdministrator or Librarian roles.
- Changes use the existing unit of work. Author deletion/link replacement share `catalog:authors`; category mutations share `catalog:categories` transaction locks.
- CAT-01 owner: creation must save at least one valid BookAuthor in the same transaction as the new book (BR-CAT-5). The replacement endpoint covers existing books, not book creation. Do not nest its service transaction inside another transaction. Coordinate any future direct link/hierarchy writes with the same locking rules.
- No DbContext, entity mappings, migrations, model snapshot, frozen contracts, or other modules changed. No Integration Owner migration is needed for this change; Domain error additions still require normal review.
- Local review of merged PR #16's Catalog registration diff found no actionable issue. Git history also contains Catalog schema PR #7. This does not identify the unnamed prerequisite PR.

## Verification

Baseline: build succeeded; Domain 241 and Application 50 tests passed. Both integration suites could not start because ASP.NET Core 8 was missing.

After these changes:

```sh
dotnet restore LibraryApp.sln --disable-parallel -m:1
dotnet build LibraryApp.sln --configuration Release --no-restore --disable-build-servers -m:1
dotnet test tests/LibraryApp.Application.Tests --configuration Release --no-build --no-restore
dotnet test tests/LibraryApp.Domain.Tests --configuration Release --no-build --no-restore
dotnet test LibraryApp.sln --configuration Release --no-build --no-restore
dotnet ef migrations has-pending-model-changes --project src/LibraryApp.Infrastructure --configuration Release --no-build
git diff --check
```

Restore succeeded. Release build: zero warnings/errors. Application: 73 passed (23 added); Domain: 241 passed. Model-sync passed: no changes since the last migration. Formatting was verified with `dotnet format whitespace LibraryApp.sln --verify-no-changes --no-restore --include` followed by the 24 changed C# paths. No new packages. No in-memory provider was used.

After ASP.NET Core 8 became available during this session, the full solution test run reached database setup. Infrastructure: 50 passed, 37 failed; API: 0 passed, 36 failed. Database fixtures fail with `System.PlatformNotSupportedException : LocalDB is not supported on this platform.` The 37 Infrastructure failures were confirmed from the TRX report using:

```sh
dotnet test tests/LibraryApp.Infrastructure.IntegrationTests --configuration Release --no-build --no-restore --logger 'console;verbosity=quiet' --logger trx --results-directory /tmp/libraryapp-role-a-results
```

All 37 Infrastructure failures have that LocalDB error. New database and API test assertions remain unverified because their fixture setup fails.

## Environment

macOS, `osx-arm64`. Initially installed: SDK 10.0.203; Microsoft.NETCore.App 8.0.26 and 10.0.7; Microsoft.AspNetCore.App 10.0.7. Exact initial error excerpt:

```text
You must install or update .NET to run this application.
App: /Users/sofi/aaaaaaaaaaaaaa/tests/LibraryApp.Infrastructure.IntegrationTests/bin/Debug/net8.0/testhost.dll
Architecture: arm64
Framework: 'Microsoft.AspNetCore.App', version '8.0.0' (arm64)
.NET location: /usr/local/share/dotnet/
The following frameworks were found:
  10.0.7 at [/usr/local/share/dotnet/shared/Microsoft.AspNetCore.App]
```

Installation commands for the existing ARM64 installation (provided, not executed):

```sh
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
sudo bash /tmp/dotnet-install.sh --channel 8.0 --runtime aspnetcore --architecture arm64 --install-dir /usr/local/share/dotnet --skip-non-versioned-files
dotnet --list-runtimes
```

Source: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script

Latest `dotnet --list-runtimes` also shows Microsoft.AspNetCore.App 8.0.31 and Microsoft.NETCore.App 8.0.31. The runtime blocker is resolved; no further runtime installation is needed. The commands above were provided, not executed by this agent.

The remaining dependency is SQL Server and `LIBRARYAPP_TEST_DB`. No repository `.env` exists; test configuration falls back to Windows-only LocalDB. `docker ps` reports that it cannot connect to the Docker daemon. The repository already supplies a SQL Server compose service, but it was not started or changed.

## Future focused PR descriptions

- `feat(catalog): seed required reference categories (S1-04)`: Add the eight required categories with stable IDs and repeatable seeding. SQL repeatability test compiles; verification awaits SQL Server.
- `feat(catalog): manage authors and book links (S1-05, CAT-02, BR-CAT-5)`: Add author CRUD and atomic nonempty book-author replacement, retaining referenced authors. Application tests pass; API and SQL verification is blocked at database setup.
- `feat(catalog): manage two-level categories (S1-06, CAT-03)`: Add category CRUD using existing hierarchy rules and restricted deletion, with serialized hierarchy changes. Application tests pass; persisted hierarchy and concurrency verification is blocked at database setup.

These are local descriptions only. All work remains on the requested current branch; any later split or publication requires a separate user request.
