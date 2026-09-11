<#
Applies database/seed/demo_data.sql (5 devices, alarm rules, ~3 days of
hourly measurements, and alarm instances in mixed triggered/acknowledged/
resolved states) to the deployed drainiq database.

Run this AFTER deploy.ps1 (migrations must already be applied - the seed
data only inserts rows into tables that already exist).

Not safe to re-run as-is: the seed data uses fixed primary keys, so running
it twice fails on duplicate keys. To reset: drop and recreate the drainiq
database, re-run deploy.ps1 (it reapplies migrations), then re-run this.
#>

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

# ===================== CONFIGURE THESE (match deploy.ps1) =====================
$PgSuperuser     = "postgres"
$PgSuperPassword = "PUT_YOUR_POSTGRES_SUPERUSER_PASSWORD_HERE"
$PgAppDb         = "drainiq"
$SqlFile         = "C:\src\drainIQ\database\seed\demo_data.sql"   # adjust if SourcePath in deploy.ps1 differs
$PgBinPath       = "C:\Program Files\PostgreSQL\16\bin"   # adjust version - `Get-ChildItem "C:\Program Files\PostgreSQL"`
# ================================================================================

$psqlExe = Join-Path $PgBinPath "psql.exe"
if (-not (Test-Path $psqlExe)) {
    throw "psql.exe not found at $psqlExe - fix `$PgBinPath above to match your PostgreSQL install."
}
if (-not (Test-Path $SqlFile)) {
    throw "Seed file not found at $SqlFile - check `$SqlFile above, or that the repo was cloned to that path."
}

$env:PGPASSWORD = $PgSuperPassword
& $psqlExe -U $PgSuperuser -h localhost -d $PgAppDb -f $SqlFile

Write-Host ""
Write-Host "Demo data applied. Login: test@drainiq.hr / Test123!@#"
