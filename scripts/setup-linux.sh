#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
CARDIO_DB_HOST="${CARDIO_DB_HOST:-localhost}"
CARDIO_DB_PORT="${CARDIO_DB_PORT:-5432}"
CARDIO_DB_ADMIN="${CARDIO_DB_ADMIN:-postgres}"
CARDIO_DOCTOR_EMAIL="${CARDIO_DOCTOR_EMAIL:-doctor@cardio.local}"
CARDIO_DOCTOR_PASSWORD="${CARDIO_DOCTOR_PASSWORD:-Doctor123456!}"
command -v psql >/dev/null || { echo 'Install PostgreSQL and its client tools first.' >&2; exit 1; }
command -v dotnet >/dev/null || { echo 'Install the .NET 10 SDK first.' >&2; exit 1; }
read -rsp "PostgreSQL administrator password: " PGPASSWORD
echo
export PGPASSWORD
trap 'unset PGPASSWORD Bootstrap__Password ConnectionStrings__Practice' EXIT
pg=(psql -X -h "$CARDIO_DB_HOST" -p "$CARDIO_DB_PORT" -U "$CARDIO_DB_ADMIN" -v ON_ERROR_STOP=1)
"${pg[@]}" -d postgres -f database/001_roles.sql
exists=$("${pg[@]}" -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='cardio'")
if [[ "$exists" != 1 ]]; then "${pg[@]}" -d postgres -f database/002_create_database.sql; fi
unmanaged=$("${pg[@]}" -d cardio -tAc "SELECT (NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='__EFMigrationsHistory') AND EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'))::int")
if [[ "$unmanaged" == 1 ]]; then
    echo 'cardio contains unmanaged tables. Back up and inspect it; setup will not overwrite them.' >&2
    exit 1
fi
"${pg[@]}" -d cardio -f database/003_permissions.sql
dotnet tool restore
dotnet restore src/Cardio.Practice --locked-mode
dotnet build src/Cardio.Practice --no-restore
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Practice="Host=$CARDIO_DB_HOST;Port=$CARDIO_DB_PORT;Database=cardio;Username=cardio_migrator;Password=M123456"
dotnet ef database update --project src/Cardio.Practice --no-build
PGPASSWORD=M123456 psql -X -h "$CARDIO_DB_HOST" -p "$CARDIO_DB_PORT" -U cardio_migrator -d cardio -v ON_ERROR_STOP=1 -f database/004_protections.sql
export ConnectionStrings__Practice="Host=$CARDIO_DB_HOST;Port=$CARDIO_DB_PORT;Database=cardio;Username=cardio_app;Password=AP123456"
export Bootstrap__Email="$CARDIO_DOCTOR_EMAIL"
export Bootstrap__Password="$CARDIO_DOCTOR_PASSWORD"
export Bootstrap__Name=Cardiologist
dotnet run --no-launch-profile --no-build --project src/Cardio.Practice -- --bootstrap
dotnet user-secrets set 'ConnectionStrings:Practice' "$ConnectionStrings__Practice" --project src/Cardio.Practice >/dev/null
echo "Ready. Doctor email: $CARDIO_DOCTOR_EMAIL"
echo 'Password is CARDIO_DOCTOR_PASSWORD (default: Doctor123456!). Existing doctor passwords are not reset.'
echo 'Start: dotnet run --project src/Cardio.Practice'
echo 'Open: http://localhost:5080'
