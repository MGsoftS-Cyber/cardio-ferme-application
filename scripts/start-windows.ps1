$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet run --project src/Cardio.Practice
    if ($LASTEXITCODE -ne 0) { throw 'The application stopped with an error.' }
}
finally { Pop-Location }
