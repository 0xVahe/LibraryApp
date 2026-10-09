# Catalog Reference Seed Mapping

Source: the authoritative 57-book list supplied by the user in this review.
All entries 1 through 57 were independently parsed before comparison with CatalogSeedData
and the mapping tables below. Exact names, completeness and source book numbers are verified.
IDs are explicit literals in CatalogSeedData and retain the reference implementation's mappings.
Existing category IDs are unchanged. No author or publisher seed IDs existed before this task.

Independent extraction found 79 author occurrences across 57 books, including 15 multi-author books,
and 75 distinct authors. The 52 books with named publishers use 27 distinct publishers.
Every required author and publisher appears exactly once in CatalogSeedData and this mapping,
with no missing or extra entries. Each co-author is a separate entry.
Publisher names containing commas stay intact. Books 53 through 57 have no named publisher.
No placeholder publisher is declared.

"Declared" means the row exists in CatalogSeedData. Every mapping row and its source book
numbers have now also been checked against the authoritative list.
No book seed exists in the current checkout. The separate downstream book-reference checks
in the "Book References" column remain pending; S1-05 reference source verification is complete.
The implementation seeds only reference rows; it does not insert books or copies.

## Authors

| Author | Fixed ID | Source Book Numbers | Declared | Book References |
|---|---|---|---|---|
| Benjamin Graham | `10000000-0000-0000-0002-000000000001` | 1 | Yes | Pending |
| Richard A. Brealey | `10000000-0000-0000-0002-000000000002` | 2 | Yes | Pending |
| Stewart C. Myers | `10000000-0000-0000-0002-000000000003` | 2 | Yes | Pending |
| Franklin Allen | `10000000-0000-0000-0002-000000000004` | 2 | Yes | Pending |
| John C. Hull | `10000000-0000-0000-0002-000000000005` | 3, 8 | Yes | Pending |
| Niall Ferguson | `10000000-0000-0000-0002-000000000006` | 4 | Yes | Pending |
| Liaquat Ahamed | `10000000-0000-0000-0002-000000000007` | 5 | Yes | Pending |
| Anat Admati | `10000000-0000-0000-0002-000000000008` | 6 | Yes | Pending |
| Martin Hellwig | `10000000-0000-0000-0002-000000000009` | 6 | Yes | Pending |
| Michael Lewis | `10000000-0000-0000-0002-000000000010` | 7 | Yes | Pending |
| Peter L. Bernstein | `10000000-0000-0000-0002-000000000011` | 9 | Yes | Pending |
| Nassim Nicholas Taleb | `10000000-0000-0000-0002-000000000012` | 10 | Yes | Pending |
| Philippe Jorion | `10000000-0000-0000-0002-000000000013` | 11 | Yes | Pending |
| Roger Lowenstein | `10000000-0000-0000-0002-000000000014` | 12 | Yes | Pending |
| Tom Burgis | `10000000-0000-0000-0002-000000000015` | 13 | Yes | Pending |
| Oliver Bullough | `10000000-0000-0000-0002-000000000016` | 14 | Yes | Pending |
| Robert C. Martin | `10000000-0000-0000-0002-000000000017` | 15, 16 | Yes | Pending |
| David Thomas | `10000000-0000-0000-0002-000000000018` | 17 | Yes | Pending |
| Andrew Hunt | `10000000-0000-0000-0002-000000000019` | 17 | Yes | Pending |
| Erich Gamma | `10000000-0000-0000-0002-000000000020` | 18 | Yes | Pending |
| Richard Helm | `10000000-0000-0000-0002-000000000021` | 18 | Yes | Pending |
| Ralph Johnson | `10000000-0000-0000-0002-000000000022` | 18 | Yes | Pending |
| John Vlissides | `10000000-0000-0000-0002-000000000023` | 18 | Yes | Pending |
| Martin Fowler | `10000000-0000-0000-0002-000000000024` | 19 | Yes | Pending |
| Eric Evans | `10000000-0000-0000-0002-000000000025` | 20 | Yes | Pending |
| Jon Skeet | `10000000-0000-0000-0002-000000000026` | 21 | Yes | Pending |
| Martin Kleppmann | `10000000-0000-0000-0002-000000000027` | 22 | Yes | Pending |
| Cole Nussbaumer Knaflic | `10000000-0000-0000-0002-000000000028` | 23 | Yes | Pending |
| Wes McKinney | `10000000-0000-0000-0002-000000000029` | 24 | Yes | Pending |
| Foster Provost | `10000000-0000-0000-0002-000000000030` | 25 | Yes | Pending |
| Tom Fawcett | `10000000-0000-0000-0002-000000000031` | 25 | Yes | Pending |
| Nate Silver | `10000000-0000-0000-0002-000000000032` | 26 | Yes | Pending |
| Ralph Kimball | `10000000-0000-0000-0002-000000000033` | 27 | Yes | Pending |
| Margy Ross | `10000000-0000-0000-0002-000000000034` | 27 | Yes | Pending |
| Charles Wheelan | `10000000-0000-0000-0002-000000000035` | 28 | Yes | Pending |
| Jim Collins | `10000000-0000-0000-0002-000000000036` | 29 | Yes | Pending |
| Patrick Lencioni | `10000000-0000-0000-0002-000000000037` | 30 | Yes | Pending |
| Andrew S. Grove | `10000000-0000-0000-0002-000000000038` | 31 | Yes | Pending |
| Kim Scott | `10000000-0000-0000-0002-000000000039` | 32 | Yes | Pending |
| Camille Fournier | `10000000-0000-0000-0002-000000000040` | 33 | Yes | Pending |
| Simon Sinek | `10000000-0000-0000-0002-000000000041` | 34 | Yes | Pending |
| Daniel H. Pink | `10000000-0000-0000-0002-000000000042` | 35 | Yes | Pending |
| Dale Carnegie | `10000000-0000-0000-0002-000000000043` | 36 | Yes | Pending |
| Kerry Patterson | `10000000-0000-0000-0002-000000000044` | 37 | Yes | Pending |
| Joseph Grenny | `10000000-0000-0000-0002-000000000045` | 37 | Yes | Pending |
| Ron McMillan | `10000000-0000-0000-0002-000000000046` | 37 | Yes | Pending |
| Al Switzler | `10000000-0000-0000-0002-000000000047` | 37 | Yes | Pending |
| Chris Voss | `10000000-0000-0000-0002-000000000048` | 38 | Yes | Pending |
| Tahl Raz | `10000000-0000-0000-0002-000000000049` | 38 | Yes | Pending |
| Roger Fisher | `10000000-0000-0000-0002-000000000050` | 39 | Yes | Pending |
| William Ury | `10000000-0000-0000-0002-000000000051` | 39 | Yes | Pending |
| Bruce Patton | `10000000-0000-0000-0002-000000000052` | 39 | Yes | Pending |
| Максим Ильяхов | `10000000-0000-0000-0002-000000000053` | 40, 41 | Yes | Pending |
| Людмила Сарычева | `10000000-0000-0000-0002-000000000054` | 40, 41 | Yes | Pending |
| Максим Дорофеев | `10000000-0000-0000-0002-000000000055` | 42 | Yes | Pending |
| Matthew Dixon | `10000000-0000-0000-0002-000000000056` | 43 | Yes | Pending |
| Nick Toman | `10000000-0000-0000-0002-000000000057` | 43 | Yes | Pending |
| Rick DeLisi | `10000000-0000-0000-0002-000000000058` | 43 | Yes | Pending |
| Fred Reichheld | `10000000-0000-0000-0002-000000000059` | 44 | Yes | Pending |
| Rob Markey | `10000000-0000-0000-0002-000000000060` | 44 | Yes | Pending |
| Tony Hsieh | `10000000-0000-0000-0002-000000000061` | 45 | Yes | Pending |
| Danny Meyer | `10000000-0000-0000-0002-000000000062` | 46 | Yes | Pending |
| Jay Baer | `10000000-0000-0000-0002-000000000063` | 47 | Yes | Pending |
| Harley Manning | `10000000-0000-0000-0002-000000000064` | 48 | Yes | Pending |
| Kerry Bodine | `10000000-0000-0000-0002-000000000065` | 48 | Yes | Pending |
| B. Joseph Pine II | `10000000-0000-0000-0002-000000000066` | 49 | Yes | Pending |
| James H. Gilmore | `10000000-0000-0000-0002-000000000067` | 49 | Yes | Pending |
| Yuval Noah Harari | `10000000-0000-0000-0002-000000000068` | 50 | Yes | Pending |
| Daniel Kahneman | `10000000-0000-0000-0002-000000000069` | 51 | Yes | Pending |
| James Clear | `10000000-0000-0000-0002-000000000070` | 52 | Yes | Pending |
| Михаил Булгаков | `10000000-0000-0000-0002-000000000071` | 53 | Yes | Pending |
| Րաֆֆի | `10000000-0000-0000-0002-000000000072` | 54 | Yes | Pending |
| Հովհաննես Թումանյան | `10000000-0000-0000-0002-000000000073` | 55 | Yes | Pending |
| Եղիշե Չարենց | `10000000-0000-0000-0002-000000000074` | 56 | Yes | Pending |
| Հրանտ Մաթևոսյան | `10000000-0000-0000-0002-000000000075` | 57 | Yes | Pending |

## Publishers

| Publisher | Fixed ID | Source Book Numbers | Declared | Book References |
|---|---|---|---|---|
| Harper Business | `10000000-0000-0000-0003-000000000001` | 1, 29, 38 | Yes | Pending |
| McGraw Hill | `10000000-0000-0000-0003-000000000002` | 2, 11, 37 | Yes | Pending |
| Pearson | `10000000-0000-0000-0003-000000000003` | 3 | Yes | Pending |
| Penguin Press | `10000000-0000-0000-0003-000000000004` | 4, 5, 26 | Yes | Pending |
| Princeton University Press | `10000000-0000-0000-0003-000000000005` | 6 | Yes | Pending |
| W. W. Norton & Company | `10000000-0000-0000-0003-000000000006` | 7, 28 | Yes | Pending |
| Wiley | `10000000-0000-0000-0003-000000000007` | 8, 9, 23, 27 | Yes | Pending |
| Random House | `10000000-0000-0000-0003-000000000008` | 10, 12, 31 | Yes | Pending |
| Harper | `10000000-0000-0000-0003-000000000009` | 13, 46, 50 | Yes | Pending |
| Profile Books | `10000000-0000-0000-0003-000000000010` | 14 | Yes | Pending |
| Prentice Hall | `10000000-0000-0000-0003-000000000011` | 15, 16 | Yes | Pending |
| Addison-Wesley | `10000000-0000-0000-0003-000000000012` | 17, 18, 19, 20 | Yes | Pending |
| Manning | `10000000-0000-0000-0003-000000000013` | 21 | Yes | Pending |
| O'Reilly Media | `10000000-0000-0000-0003-000000000014` | 22, 24, 25, 33 | Yes | Pending |
| Jossey-Bass | `10000000-0000-0000-0003-000000000015` | 30 | Yes | Pending |
| St. Martin's Press | `10000000-0000-0000-0003-000000000016` | 32 | Yes | Pending |
| Portfolio | `10000000-0000-0000-0003-000000000017` | 34, 43, 47 | Yes | Pending |
| Riverhead Books | `10000000-0000-0000-0003-000000000018` | 35 | Yes | Pending |
| Simon & Schuster | `10000000-0000-0000-0003-000000000019` | 36 | Yes | Pending |
| Penguin Books | `10000000-0000-0000-0003-000000000020` | 39 | Yes | Pending |
| Альпина Паблишер | `10000000-0000-0000-0003-000000000021` | 40, 41 | Yes | Pending |
| Манн, Иванов и Фербер | `10000000-0000-0000-0003-000000000022` | 42 | Yes | Pending |
| Harvard Business Review Press | `10000000-0000-0000-0003-000000000023` | 44, 49 | Yes | Pending |
| Grand Central Publishing | `10000000-0000-0000-0003-000000000024` | 45 | Yes | Pending |
| New Harvest | `10000000-0000-0000-0003-000000000025` | 48 | Yes | Pending |
| Farrar, Straus and Giroux | `10000000-0000-0000-0003-000000000026` | 51 | Yes | Pending |
| Avery | `10000000-0000-0000-0003-000000000027` | 52 | Yes | Pending |

## Verification and Workflow

- Initial creation, repeatability with a fresh context, partial initialization, preservation of edited and unrelated rows, and category seeding are covered by CatalogReferenceSeedTests using SQL Server, the module registration and the startup initializer.
- CatalogSeedDataTests locks the reference name-to-ID mappings and checks names and IDs for duplicates without requiring SQL Server.
- Runtime SQL integration verification requires a configured LIBRARYAPP_TEST_DB. LocalDB is unsupported on this macOS machine.
- Local verification completed with an isolated SQL Server 2022 Docker container and LIBRARYAPP_TEST_DB supplied only to the test process. The container was removed after the tests.
- Previous review validation: Release build with 0 warnings and 0 errors. Domain: 241 passed; Application: 109 passed; Infrastructure: 109 passed; API: 44 passed. Total: 503 passed, 0 failed, 0 skipped in those runs.
- All four CatalogReferenceSeedTests passed, including initial seed, restart with a fresh context, partial seed, and preservation of edited names, category parent and unrelated rows. All five CatalogSeedDataTests passed.
- Earlier attempts using the default LocalDB were blocked during setup (4 new seed tests and 6 existing regression cases). Their assertions were not treated as passed; all passed after configuring SQL Server.
- Current source review: exact source sets, individual co-authors, comma-containing publishers, absence of the "no publisher" placeholder, all documented source book numbers and fixed ID mappings match. No seed data or ID correction was needed.
- Current review checks: CatalogSeedDataTests rebuilt and rerun in Release; 5 passed, 0 failed, 0 skipped. The prior full-solution build and 503-test result were not rerun during this source-only review.
- The development-base prerequisite is satisfied in this checkout: commit 37588f0 includes the authors-and-categories merge (PR #23).
- One Group 1 teammate's approval and the final PR merge into development remain pending.
