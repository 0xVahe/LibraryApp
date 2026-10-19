#!/usr/bin/env bash
# Smoke test of the system started by `docker compose up` in Development (NFR-13): the API answers, Swagger is served,
# errors are ProblemDetails, the pages work, and the start applied every migration and the seed data.
#
# Usage: smoke-test.sh <api base url> <SQL Server container> <sa password>
# Example: smoke-test.sh http://localhost:8080 libraryapp-sqlserver "$MSSQL_SA_PASSWORD"
set -uo pipefail

BASE="$1"
SQL_CONTAINER="$2"
SA_PASSWORD="$3"
MIGRATIONS_DIR="src/LibraryApp.Infrastructure/Persistence/Migrations"
failures=0
body=$(mktemp "${RUNNER_TEMP:-.}/smoke.XXXXXX")   # RUNNER_TEMP on GitHub; the current folder elsewhere

pass() { echo "✅ $1"; }
fail() { echo "::error::$1"; failures=$((failures + 1)); }

# expect <method> <path> <status> [content type] [text in the body]
expect() {
  local method="$1" path="$2" status="$3" type="${4:-}" text="${5:-}" result code ctype
  result=$(curl -s -o "$body" -w '%{http_code} %{content_type}' -X "$method" "$BASE$path")
  code="${result%% *}"
  ctype="${result#* }"
  if [ "$code" != "$status" ]; then
    fail "$method $path answered $code instead of $status"
    return
  fi
  if [ -n "$type" ] && [[ "$ctype" != "$type"* ]]; then
    fail "$method $path answered $ctype instead of $type"
    return
  fi
  if [ -n "$text" ] && ! grep -q -- "$text" "$body"; then
    fail "$method $path: the answer has no \"$text\""
    return
  fi
  pass "$method $path → $code${type:+ $type}"
}

sql() {
  docker exec "$SQL_CONTAINER" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -d LibraryApp \
    -h -1 -W -b -Q "SET NOCOUNT ON; $1" | tr -d '[:space:]'
}

echo "### API"
expect GET /health/live 200
expect GET /health/ready 200
expect GET /swagger/v1/swagger.json 200 application/json '"openapi"'
expect GET /swagger/index.html 200 text/html
# NFR-02: an API endpoint without a token answers 401 as ProblemDetails (SD-05).
expect GET /api/books 401 application/problem+json
expect GET /api/this-route-does-not-exist 404 application/problem+json
# Pages: sign-in is anonymous; the home page sends an anonymous visitor to sign in.
expect GET /login 200 text/html
expect GET / 302

echo "### Database after the start"
applied=$(sql "SELECT COUNT(*) FROM __EFMigrationsHistory")
expected=$(find "$MIGRATIONS_DIR" -maxdepth 1 -name '[0-9]*_*.cs' ! -name '*.Designer.cs' | wc -l | tr -d ' ')
if [ "$applied" = "$expected" ]; then
  pass "migrations applied: $applied of $expected"
else
  fail "migrations applied: '$applied', expected $expected (files in $MIGRATIONS_DIR)"
fi
for table in Books BookCopies StaffMembers Loans NotificationTemplates; do
  rows=$(sql "SELECT COUNT(*) FROM $table")
  if [[ "$rows" =~ ^[0-9]+$ ]] && [ "$rows" -gt 0 ]; then
    pass "seed data: $table has $rows rows"
  else
    fail "seed data: $table has '$rows' rows, expected some"
  fi
done

rm -f "$body"
if [ "$failures" -gt 0 ]; then
  echo "::error::$failures smoke check(s) failed"
  exit 1
fi
echo "All smoke checks passed"
