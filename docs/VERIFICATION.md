# Verification — 23 October 2026

## Results obtained in this environment

| Check | Result |
| --- | --- |
| .NET SDK used | 10.0.402 |
| Restore using the committed package lock | Passed |
| Build of Cardio.slnx | Passed: 0 warnings, 0 errors |
| Initial EF migration generation | Passed; included with its designer and model snapshot |
| EF model versus latest migration | No pending model changes |
| Generate idempotent PostgreSQL schema SQL | Passed |
| Start compiled ASP.NET application | Passed |
| GET / | HTTP 200, staff sign-in page returned |
| GET /app.js and /site.css | HTTP 200 |
| GET /api/patients without authentication | HTTP 401 |
| JavaScript syntax using node --check | Passed |
| Python integration test syntax | Passed |
| Bash setup syntax using bash -n | Passed |
| Windows PowerShell setup/start grammar parsing | Passed using tree-sitter-powershell |
| All shipped SQL file grammar parsing | Passed using pglast |

SQL grammar parsing does not execute statements or validate PostgreSQL privileges,
extensions or function behavior. PowerShell grammar parsing is not execution on
Windows. The integration test was syntax-checked, not run against a live database.

## Corrections and additions

- Reconstructed actual files from the attached source, rather than empty placeholders.
- Removed Docker configuration and replaced setup with native PostgreSQL scripts.
- Replaced psql-only backslash commands with pgAdmin-compatible SQL.
- Split database creation from transaction-safe permissions/extension setup.
- Added the requested cardio_app/AP123456 and cardio_migrator/M123456 credentials.
- Added committed migrations, a design-time DbContext factory and generated schema SQL.
- Added permissions for migrations into an existing empty database owned by postgres.
- Kept runtime access separate from migration/schema ownership.
- Added a positive appointment-period constraint.
- Separated the document row lock from EF's owned prescription-line query, avoiding
  a problematic FOR UPDATE query composed with joins.
- Made exclusion-constraint installation independently repeatable.
- Tightened the office-or-patient authorization policy to explicitly accept
  role-free patient accounts rather than every account without a Nurse role.
- Health readiness checks pending migrations, in addition to connectivity.
- Added local launch settings, pinned package versions, a dependency lock and EF tools manifest.
- Added fail-fast setup scripts, bootstrap login instructions, and build CI without containers.
- Extended integration tests to exercise overlapping slots and prescription signing.

## Remaining verification on your PC

A live PostgreSQL server was not available for successful integration testing here.
Database creation, migrations, role permissions, exclusion constraints, triggers,
doctor bootstrap and authenticated workflows therefore still require execution on
your installation. The Windows setup script has not been executed on Windows.

Use docs/START_HERE.md, then run tests/smoke.py against a disposable installation
containing only synthetic data. Keep PostgreSQL running while the app runs. The
test creates synthetic patients, accounts, bookings, documents and payments; it
does not remove them. Do not run it against real patient data.

The application is a development baseline for the supplied scope, not a fully
validated medical production system. Scanner/device connections, specialized
cardiology forms, medication catalogues, production delivery/scanning services,
and recognized electronic signatures require additional work.
