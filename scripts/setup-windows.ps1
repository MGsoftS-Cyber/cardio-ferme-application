param(
    [string]$DbHost = 'localhost',
    [ValidateRange(1,65535)][int]$DbPort = 5432,
    [string]$AdminUser = 'postgres',
    [string]$DoctorEmail = 'doctor@cardio.local',
    [string]$DoctorPassword = 'Doctor123456!'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Checked([scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Command failed (exit $LASTEXITCODE). Setup stopped." }
}

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/Cardio.Practice'
$psql = (Get-Command psql -ErrorAction SilentlyContinue)
if ($psql) { $psql = $psql.Source }
else {
    $psql = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$psql) { throw 'psql.exe not found. Install PostgreSQL command line tools or add its bin folder to PATH.' }
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 10 SDK first.' }

$names = @('PGPASSWORD','ConnectionStrings__Practice','ASPNETCORE_ENVIRONMENT',
    'Bootstrap__Email','Bootstrap__Password','Bootstrap__Name')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
$securePassword = Read-Host 'Password for your PostgreSQL administrator (postgres)' -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
Push-Location $root
try {
    $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    $pgArgs = @('-X','-h',$DbHost,'-p',"$DbPort",'-U',$AdminUser,'-v','ON_ERROR_STOP=1')
    Invoke-Checked { & $psql @pgArgs -d postgres -f database/001_roles.sql }
    $exists = & $psql @pgArgs -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='cardio'"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot check whether cardio exists.' }
    if (($exists -join '').Trim() -ne '1') {
        Invoke-Checked { & $psql @pgArgs -d postgres -f database/002_create_database.sql }
    }
    $unmanaged = & $psql @pgArgs -d cardio -tAc "SELECT (NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='__EFMigrationsHistory') AND EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'))::int"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect cardio.' }
    if (($unmanaged -join '').Trim() -eq '1') { throw 'cardio contains tables without EF migration history. Back up and inspect it; setup will not overwrite it.' }
    Invoke-Checked { & $psql @pgArgs -d cardio -f database/003_permissions.sql }
    Invoke-Checked { dotnet tool restore }
    Invoke-Checked { dotnet restore src/Cardio.Practice --locked-mode }
    Invoke-Checked { dotnet build src/Cardio.Practice --no-restore }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__Practice = "Host=$DbHost;Port=$DbPort;Database=cardio;Username=cardio_migrator;Password=M123456"
    Invoke-Checked { dotnet ef database update --project src/Cardio.Practice --no-build }
    # Run as the migrator so functions and protection triggers have the same owner.
    $env:PGPASSWORD = 'M123456'
    $migrateArgs = @('-X','-h',$DbHost,'-p',"$DbPort",'-U','cardio_migrator','-d','cardio','-v','ON_ERROR_STOP=1')
    Invoke-Checked { & $psql @migrateArgs -f database/004_protections.sql }
    $runtime = "Host=$DbHost;Port=$DbPort;Database=cardio;Username=cardio_app;Password=AP123456"
    $env:ConnectionStrings__Practice = $runtime
    $env:Bootstrap__Email = $DoctorEmail
    $env:Bootstrap__Password = $DoctorPassword
    $env:Bootstrap__Name = 'Cardiologist'
    Invoke-Checked { dotnet run --no-launch-profile --no-build --project src/Cardio.Practice '--' '--bootstrap' }
    # Persist only the runtime connection in local Development settings.
    @{ ConnectionStrings = @{ Practice = $runtime } } | ConvertTo-Json -Depth 3 |
        Set-Content (Join-Path $project 'appsettings.Development.json') -Encoding UTF8
    Write-Host "Ready. Doctor email: $DoctorEmail"
    Write-Host 'The doctor password is the DoctorPassword argument (default: Doctor123456!). Existing doctor passwords are not reset.'
    Write-Host 'Start: dotnet run --project src/Cardio.Practice'
    Write-Host 'Open: http://localhost:5080'
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
    Pop-Location
}
