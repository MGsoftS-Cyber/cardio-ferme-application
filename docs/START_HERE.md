# Start here — Windows, pgAdmin and the code

## 1. What to do first

Install PostgreSQL directly on your PC with pgAdmin and its command-line tools,
and install the .NET 10 SDK. Start the PostgreSQL Windows service. Docker is not
needed. Open PowerShell and check `dotnet --version`; it should start with 10.
Keep the administrator password you chose when installing PostgreSQL.

Extract the ZIP before running anything. Use a folder such as
`C:\Projects\cardio-postgresql`, not the ZIP preview window.
Open PowerShell in the folder containing README.md and Cardio.slnx.

## 2. Recommended automatic setup

Read scripts/setup-windows.ps1, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-windows.ps1
```

When prompted, enter your current PostgreSQL administrator password. This is
the password for postgres, not AP123456 and not M123456 unless you deliberately
used one of those during PostgreSQL installation.

The script performs these operations in order:

1. Creates the two PostgreSQL roles if missing and assigns your requested passwords.
2. Creates cardio only if it does not already exist. It does not drop databases.
3. Checks for existing unmanaged tables and stops rather than overwriting them.
4. Creates btree_gist and configures schema/default permissions.
5. Restores packages and EF tools, builds, and applies the included migrations.
6. Adds PostgreSQL booking constraints and document/audit/payment protections.
7. Creates the first doctor through ASP.NET Identity using the runtime role.
8. Saves the runtime connection to appsettings.Development.json.

If you already created an **empty** cardio database in pgAdmin, keep it: the
script can use it. If it contains old tables, back it up and inspect it before
changing its schema. Do not delete migration history to bypass an error.

## 3. The exact connection values

| Setting | Runtime app | Migration tool |
| --- | --- | --- |
| Host/server address | localhost | localhost |
| Port | 5432 | 5432 |
| Database | cardio | cardio |
| Schema | public | public |
| PostgreSQL username | cardio_app | cardio_migrator |
| PostgreSQL password | AP123456 | M123456 |

Runtime connection:

```text
Host=localhost;Port=5432;Database=cardio;Username=cardio_app;Password=AP123456
```

Migration connection:

```text
Host=localhost;Port=5432;Database=cardio;Username=cardio_migrator;Password=M123456
```

In src/Cardio.Practice/appsettings.Development.json:

```json
{
  "ConnectionStrings": {
    "Practice": "Host=localhost;Port=5432;Database=cardio;Username=cardio_app;Password=AP123456"
  }
}
```

Program.cs reads it and passes it to Entity Framework's PostgreSQL provider:

```csharp
builder.Services.AddDbContext<PracticeDb>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Practice")
        ?? throw new InvalidOperationException("Set ConnectionStrings:Practice.")));
```

The flow is browser -> ASP.NET API -> EF Core/Npgsql -> PostgreSQL.
The browser does not receive the PostgreSQL connection string.
Future Laravel/Flutter apps should use authenticated API calls, never embed
database usernames or passwords in a browser/mobile application.

For PostgreSQL installed on another computer, localhost must become that
computer's actual address. Configure PostgreSQL network access and TLS for that
deployment; this package defaults to a local development installation.

## 4. Start and log in

```powershell
dotnet run --project src/Cardio.Practice
```

Open http://localhost:5080 and sign in with:

- Email: doctor@cardio.local
- Password: Doctor123456!

The account exists only after setup/bootstrap succeeds. These are web login
credentials, separate from PostgreSQL credentials. Bootstrap leaves an existing
doctor's password unchanged. Ctrl+C in the terminal stops the application.

You can also open Cardio.slnx or src/Cardio.Practice/Cardio.Practice.csproj in
a .NET 10 compatible Visual Studio installation and use the Cardio.Practice profile.

## 5. Manual pgAdmin alternative

Use this route instead of automatic setup if you want to see each database step.
Do not mix the generated schema alternative with a second manually created schema.

In pgAdmin, connect to PostgreSQL as **postgres**. A registered server's display
name can be anything; the actual address is localhost and its actual port is
normally 5432. Query Tool works after selecting a **database**, not the top-level
Servers collection or PostgreSQL installation entry.

1. Expand your server -> Databases -> select postgres -> right-click Query Tool.
   Open database/001_roles.sql and execute it.
2. If cardio does not exist, open database/002_create_database.sql in that tool.
   Enable Auto-commit and run the single CREATE DATABASE statement. Do not wrap
   it in BEGIN/COMMIT. Alternatively, right-click Databases -> Create -> Database,
   name cardio, owner cardio_migrator. Skip this step if cardio already exists.
3. Refresh Databases, select **cardio**, then open a new Query Tool as postgres.
   Run database/003_permissions.sql.
4. In PowerShell, from the extracted project root, run:

```powershell
dotnet tool restore
dotnet restore src/Cardio.Practice --locked-mode
dotnet build src/Cardio.Practice --no-restore
$env:ConnectionStrings__Practice = 'Host=localhost;Port=5432;Database=cardio;Username=cardio_migrator;Password=M123456'
dotnet ef database update --project src/Cardio.Practice --no-build
```

The initial migration is already included. Do **not** run migrations add
InitialCreate again. EF creates AspNetUsers and the other application tables.

If you prefer SQL to EF CLI, **instead of database update**, run
database/schema/InitialCreate.sql in the cardio Query Tool as postgres.
That generated script sets the role to cardio_migrator so table ownership and
default grants are correct. It also records the applied EF migration.

5. In the cardio Query Tool as postgres, execute:

```sql
SET ROLE cardio_migrator;
```

Then open and execute database/004_protections.sql **in that same Query Tool
session**, then execute `RESET ROLE;`. The tables must already exist.
These protections are required for booking conflicts and immutable records.

6. Back in PowerShell, switch to the runtime connection and create the doctor:

```powershell
$env:ConnectionStrings__Practice = 'Host=localhost;Port=5432;Database=cardio;Username=cardio_app;Password=AP123456'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Bootstrap__Email = 'doctor@cardio.local'
$env:Bootstrap__Password = 'Doctor123456!'
$env:Bootstrap__Name = 'Cardiologist'
dotnet run --no-launch-profile --no-build --project src/Cardio.Practice -- --bootstrap
Remove-Item Env:Bootstrap__Password
dotnet run --project src/Cardio.Practice
```

Local defaults are already in appsettings.Development.json for future terminals.
If you use a different host/port, update that JSON too. User secrets and process
environment variables take precedence over JSON: check old overrides if necessary.

## 6. Verify the installation

Open http://localhost:5080/health: expect {"status":"ok"} after migrations.
In pgAdmin, refresh cardio -> Schemas -> public -> Tables. Expect AspNetUsers,
AspNetRoles, Patients, Slots, Appointments, Documents, and __EFMigrationsHistory,
plus the other clinical, office, and identity tables. Some names require quotes
because EF uses mixed case:

```sql
SELECT * FROM "__EFMigrationsHistory";
SELECT "Email" FROM "AspNetUsers";
SELECT current_database(), current_user;
```

Log in, create a synthetic patient, create a future slot, book it, save a
consultation, create/finalize a certificate, and record an invoice/payment.
Use tests/smoke.py against a disposable database for integration workflows;
the test adds records and does not remove them. A passing build alone does not
prove that permissions, triggers or clinical workflows work on your installation.

## 7. Common errors

| Error | What to check |
| --- | --- |
| Connection refused | PostgreSQL service running; correct host and actual port |
| Password authentication failed | Correct role/password; old environment or user-secrets override |
| Database cardio does not exist | Run database creation before migrations |
| relation AspNetUsers does not exist | Apply the included migrations to the same database used by the running app, before bootstrap |
| Permission denied for schema public | Run 003_permissions.sql as postgres; migrate as cardio_migrator |
| Permission denied for table | Verify table owner is cardio_migrator; inspect existing installation rather than grant administrator rights to cardio_app |
| CREATE DATABASE cannot run inside a transaction | Run 002_create_database.sql alone with Auto-commit ON |
| dotnet command missing | Install SDK, not only runtime; reopen terminal |
| psql command missing | Install PostgreSQL tools or add its bin folder to PATH; Windows setup also searches Program Files |
| Web sign-in failed | Use the doctor credentials, not the database credentials; confirm bootstrap completed |
| HTTP 401 after inactivity | This baseline's access tokens expire after 15 minutes; sign in again |
| Existing unmanaged tables | Back up and inspect the database; do not delete tables or migration history blindly |

## 8. Later schema changes

Edit models, then use the migrator connection for `dotnet ef migrations add
DescriptiveChangeName --project src/Cardio.Practice` and `dotnet ef database
update --project src/Cardio.Practice`. Reapply 004_protections.sql after migration
changes and review any affected constraints/triggers. Switch back to cardio_app
before starting the website. Never give the runtime role superuser rights.
