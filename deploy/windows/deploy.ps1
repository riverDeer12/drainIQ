<#
Deploys the drainIQ API onto IIS as a sub-application under an already
existing site (so the existing index.html keeps serving at the domain
root, and the API becomes reachable at http://<domain>/api/...).

Run this in an elevated PowerShell session (Run as Administrator) on the
Windows Server - either manually, or via the self-hosted GitHub Actions
runner configured to run as an admin-capable account (see
.github/workflows/deploy.yml, which calls this with -PgSuperPassword from
a GitHub Actions secret).

Prerequisites (not handled by this script - see the deploy chat notes):
  - PostgreSQL installed and running (GUI installer), with a known
    superuser password.
  - Git for Windows installed (git.exe on PATH).
  - .NET 10 SDK installed (dotnet.exe on PATH) - the ASP.NET Core Hosting
    Bundle alone only ships the runtime, which is not enough to run
    `dotnet publish`.
  - IIS with the existing site already created.

Safe to re-run: skips creating things that already exist, re-publishes over
the same folder to pick up new code (git pull + dotnet publish), and reuses
the JWT signing key already in web.config from the previous deploy instead
of generating a new one each time - a fresh key on every run would log
every user out on every redeploy.
#>

#Requires -RunAsAdministrator

param(
    [string]$SiteName = "drainiq.eu",           # `Get-Website | Select Name` to list existing sites
    [string]$AppPoolName = "drainIQApiPool",
    [string]$AppName = "api",                    # API is reachable at http://yourdomain/api/...
    [string]$RepoUrl = "https://github.com/riverDeer12/drainIQ.git",
    [string]$SourcePath = "C:\src\drainIQ",      # where the repo is cloned/built, kept outside wwwroot
    [string]$PgSuperuser = "postgres",
    [Parameter(Mandatory)][string]$PgSuperPassword,
    [string]$PgAppDb = "drainiq",
    [string]$PgBinPath = "C:\Program Files\PostgreSQL\18\bin"   # adjust version - `Get-ChildItem "C:\Program Files\PostgreSQL"`
)

$ErrorActionPreference = "Stop"

# $ErrorActionPreference only affects PowerShell cmdlets, not the exit code
# of native executables (dotnet.exe, git.exe, ...) - without this, a failed
# `dotnet publish` would print its error and the script would carry on
# regardless. Call external commands through here so failures actually stop.
function Invoke-Checked {
    param([Parameter(Mandatory)][ScriptBlock]$Command, [string]$ErrorMessage = "Command failed")
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$ErrorMessage (exit code $LASTEXITCODE)"
    }
}

# In-process hosting loads drainIQ.dll straight into the IIS worker process,
# so `dotnet publish` can't overwrite it on a redeploy while the app pool is
# running - it stays locked until the pool actually stops.
function Stop-AppPoolIfRunning {
    param([string]$Name)
    if (-not (Test-Path "IIS:\AppPools\$Name")) {
        return
    }
    if ((Get-WebAppPoolState -Name $Name).Value -ne "Stopped") {
        Write-Host "    Stopping app pool '$Name' to release the lock on drainIQ.dll..."
        Stop-WebAppPool -Name $Name
        $deadline = (Get-Date).AddSeconds(15)
        while ((Get-WebAppPoolState -Name $Name).Value -ne "Stopped" -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 500
        }
        Start-Sleep -Seconds 2   # small safety margin past the reported Stopped state
    }
}

Import-Module WebAdministration

$psqlExe = Join-Path $PgBinPath "psql.exe"
$createdbExe = Join-Path $PgBinPath "createdb.exe"
if (-not (Test-Path $psqlExe)) {
    throw "psql.exe not found at $psqlExe - fix `$PgBinPath above to match your PostgreSQL install."
}

$siteIisPath = "IIS:\Sites\$SiteName"
if (-not (Test-Path $siteIisPath)) {
    throw "Site '$SiteName' not found. Run 'Get-Website' to list existing sites and fix `$SiteName above."
}
$SitePhysicalPath = (Get-Item $siteIisPath).PhysicalPath
$PublishPath = Join-Path $SitePhysicalPath $AppName
$ProjectPath = Join-Path $SourcePath "drainIQ"
$env:PGPASSWORD = $PgSuperPassword

Write-Host "==> 1/6 Creating PostgreSQL database '$PgAppDb' (if missing)..."
$dbExists = & $psqlExe -U $PgSuperuser -h localhost -tAc "SELECT 1 FROM pg_database WHERE datname='$PgAppDb'"
if ($dbExists -ne "1") {
    Invoke-Checked { & $createdbExe -U $PgSuperuser -h localhost $PgAppDb } "createdb failed"
    Write-Host "    Database '$PgAppDb' created."
} else {
    Write-Host "    Database '$PgAppDb' already exists, skipping."
}

Write-Host "==> 2/6 Fetching source ($RepoUrl)..."
if (Test-Path $SourcePath) {
    Invoke-Checked { git -C $SourcePath pull } "git pull failed"
} else {
    Invoke-Checked { git clone $RepoUrl $SourcePath } "git clone failed"
}

Write-Host "==> 3/6 Publishing the API (Release)..."
Stop-AppPoolIfRunning -Name $AppPoolName

# `dotnet publish` overwrites web.config wholesale, so grab the JWT key it
# already has (if any) before that happens - reusing it, rather than
# generating a new one every deploy, which would invalidate every issued
# token (log everyone out) on every redeploy.
$webConfigPath = Join-Path $PublishPath "web.config"
$ExistingJwtKey = $null
if (Test-Path $webConfigPath) {
    try {
        [xml]$oldWebConfig = Get-Content $webConfigPath
        $existingNode = $oldWebConfig.SelectSingleNode("//environmentVariable[@name='Jwt__Key']")
        if ($existingNode) {
            $ExistingJwtKey = $existingNode.GetAttribute("value")
        }
    } catch {
        Write-Host "    Could not read the previous web.config, will generate a new JWT key."
    }
}

New-Item -ItemType Directory -Force -Path $PublishPath | Out-Null
Invoke-Checked { dotnet publish $ProjectPath -c Release -o $PublishPath } "dotnet publish failed"

Write-Host "==> 4/6 Writing connection string, JWT key and environment into web.config..."
$connString = "Host=localhost;Port=5432;Database=$PgAppDb;Username=$PgSuperuser;Password=$PgSuperPassword"

if ($ExistingJwtKey) {
    $JwtKey = $ExistingJwtKey
    Write-Host "    Reusing the JWT signing key from the previous deploy."
} else {
    $bytes = New-Object byte[] 32
    (New-Object Security.Cryptography.RNGCryptoServiceProvider).GetBytes($bytes)
    $JwtKey = [Convert]::ToBase64String($bytes)
    Write-Host "    No existing JWT key found - generated a new one (expected on the first-ever deploy)."
}

[xml]$webConfig = Get-Content $webConfigPath
$aspNetCoreNode = $webConfig.SelectSingleNode("//aspNetCore")
if (-not $aspNetCoreNode) {
    throw "Could not find <aspNetCore> in $webConfigPath - inspect the file, the generated structure may differ."
}

$envVarsNode = $webConfig.CreateElement("environmentVariables")
function Add-EnvVar($doc, $parent, $name, $value) {
    $node = $doc.CreateElement("environmentVariable")
    $node.SetAttribute("name", $name)
    $node.SetAttribute("value", $value)
    $parent.AppendChild($node) | Out-Null
}
Add-EnvVar $webConfig $envVarsNode "ASPNETCORE_ENVIRONMENT" "Production"
Add-EnvVar $webConfig $envVarsNode "ConnectionStrings__DefaultConnection" $connString
Add-EnvVar $webConfig $envVarsNode "Jwt__Key" $JwtKey
$aspNetCoreNode.AppendChild($envVarsNode) | Out-Null
$webConfig.Save($webConfigPath)

Write-Host "    Current JWT signing key: $JwtKey"

Write-Host "==> 5/6 Configuring IIS application pool and application..."
if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    New-WebAppPool -Name $AppPoolName | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""

if (Get-WebApplication -Site $SiteName -Name $AppName -ErrorAction SilentlyContinue) {
    Write-Host "    Application '$AppName' already exists under site '$SiteName', reusing it."
} else {
    New-WebApplication -Site $SiteName -Name $AppName -PhysicalPath $PublishPath -ApplicationPool $AppPoolName | Out-Null
}

Write-Host "==> 6/6 Applying EF Core migrations..."
if (-not (dotnet tool list -g | Select-String "dotnet-ef")) {
    dotnet tool install --global dotnet-ef
}
Push-Location $ProjectPath
try {
    # Program.cs throws at startup if Jwt:Key is missing, regardless of
    # whether this particular operation needs it - so `dotnet ef` needs it
    # set too, even though a migration update never touches JWT code.
    $env:ConnectionStrings__DefaultConnection = $connString
    $env:Jwt__Key = $JwtKey
    Invoke-Checked { dotnet ef database update } "dotnet ef database update failed"
} finally {
    Pop-Location
}

if ((Get-WebAppPoolState -Name $AppPoolName).Value -ne "Started") {
    Start-WebAppPool -Name $AppPoolName
}

Write-Host ""
Write-Host "Done. API is live under: http://<server-or-domain>/$AppName/..."
Write-Host "ASPNETCORE_ENVIRONMENT is 'Production', so /scalar/v1 and /openapi/v1.json are NOT mapped there on purpose"
Write-Host "(they're dev-only in Program.cs). Change that environment variable in web.config to 'Development' if you"
Write-Host "want Scalar reachable there too, e.g. for demoing to the city - just be aware it also re-enables the"
Write-Host "auto-seeder in DbSeeder.cs, which no-ops once any device row exists, so it's harmless after the first run."
Write-Host ""
Write-Host "To load the demo/test dataset, run seed-demo-data.ps1 next."
