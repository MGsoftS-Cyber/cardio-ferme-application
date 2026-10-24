# Cardiology Practice — ASP.NET Core + native PostgreSQL

This ZIP contains the actual C# code, doctor/staff web interface, API,
committed EF migrations, SQL files and setup instructions. No Docker required.
Read **docs/START_HERE.md** for the detailed installation and pgAdmin steps.
Read **docs/VERIFICATION.md** for checks performed and test limits.

## Windows quick start

Install the .NET 10 SDK and PostgreSQL 16+ with pgAdmin and command-line tools.
Keep the postgres administrator password chosen during installation.
Extract the ZIP, then open PowerShell in the folder containing this README:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-windows.ps1
dotnet run --project src/Cardio.Practice
```

The execution policy option is for that one process. Review the script before
running it. It prompts for the existing postgres password, creates the database
and roles if missing, applies migrations and protections, and creates a doctor.
Open **http://localhost:5080**.

| Purpose | Username/email | Password |
| --- | --- | --- |
| Runtime PostgreSQL account | cardio_app | AP123456 |
| Migration PostgreSQL account | cardio_migrator | M123456 |
| First doctor web login | doctor@cardio.local | Doctor123456! |
| PostgreSQL administrator | postgres | Your installation password |

Database: cardio. Host: localhost. Port: 5432. Schema: public.
The database passwords are separate from the doctor web login.
The supplied passwords are for local development; change them before deployment.
The bootstrap does not reset an existing doctor's password.

Custom port or doctor account:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-windows.ps1 -DbPort 5433 -DoctorEmail you@example.com -DoctorPassword 'YourLongPassword123!'
```

## Ubuntu/Linux

After installing native PostgreSQL, client tools and the .NET 10 SDK:

```bash
bash scripts/setup-linux.sh
dotnet run --project src/Cardio.Practice
```

The script uses TCP/password authentication for the administrator. Override
CARDIO_DB_HOST, CARDIO_DB_PORT, CARDIO_DB_ADMIN, CARDIO_DOCTOR_EMAIL and
CARDIO_DOCTOR_PASSWORD as needed. Its runtime connection is saved in local
.NET development user secrets. Environment variables and user secrets override
appsettings.Development.json; update old overrides if connection errors persist.

## Included workflows

Identity accounts and Doctor/Nurse/Receptionist roles; patients and verified
portal account linking; appointment slots and bookings; clinical entries and BMI;
consultations; prescription/certificate/referral finalization and printing;
private PDF reports; invoices, payments, messages, notifications and audit events.

This is a development baseline. Specialized cardiology forms, scanner/device
connections, medication databases, production email/SMS and recognized electronic
signatures require further integration. PDF uploads are enabled in Development
only. Laravel and Flutter clients are not implemented in this ZIP; they should
call the API, never PostgreSQL directly. Use synthetic patients for tests.

## Files

- src/Cardio.Practice: backend and staff workspace.
- src/Cardio.Practice/Data/Migrations: initial migration and model snapshot.
- database/001_roles.sql: run as postgres connected to postgres.
- database/002_create_database.sql: only if cardio is missing; Auto-commit ON.
- database/003_permissions.sql: run as postgres connected to cardio.
- database/schema/InitialCreate.sql: generated schema alternative for pgAdmin.
- database/004_protections.sql: run AFTER migrations/schema as cardio_migrator.
- scripts: setup and Windows start commands.
- tests/smoke.py: integration test creating synthetic records.
- docs: detailed setup, connection and verification notes.

## Integration tests

Only use a disposable installation containing synthetic data. Start the app,
then in a second PowerShell window:

```powershell
$env:CARDIO_DOCTOR_EMAIL = 'doctor@cardio.local'
$env:CARDIO_DOCTOR_PASSWORD = 'Doctor123456!'
python tests/smoke.py
```

These tests create records and do not clean them up. They cover patient access,
concurrent booking, BMI validation, document release and payment idempotency.

References:
https://www.npgsql.org/efcore/
https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying
