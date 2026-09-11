<#
Deploys the drainIQ API onto IIS as a sub-application under an already
existing site (so the existing index.html keeps serving at the domain
root, and the API becomes reachable at http://<domain>/api/...).

Run this in an elevated PowerShell session (Run as Administrator) on the
Windows Server.

Prerequisites (not handled by this script - see the deploy chat notes):
  - PostgreSQL installed and running (GUI installer), with a known
    superuser password.
  - Git for Windows installed (git.exe on PATH).
  - .NET 10 SDK installed (dotnet.exe on PATH) - the ASP.NET Core Hosting
    Bundle alone only ships the runtime, which is not enough to run
    `dotnet publish`.
  - IIS with the existing site already created.

Safe to re-run: skips creating things that already exist, and re-publishes
over the same folder to pick up new code (git pull + dotnet publish).
#>

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

# ===================== CONFIGURE THESE =====================
$SiteName        = "YourExistingSiteName"   # `Get-Website | Select Name` to list existing sites
$AppPoolName     = "drainIQApiPool"
$AppName         = "api"                    # API will be reachable at http://yourdomain/api/...
$RepoUrl         = "https://github.com/riverDeer12/drainIQ.git"
$SourcePath      = "C:\src\drainIQ"         # where the repo is cloned/built, kept outside wwwroot
$PgSuperuser     = "postgres"
$PgSuperPassword = "PUT_YOUR_POSTGRES_SUPERUSER_PASSWORD_HERE"
$PgAppDb         = "drainiq"
$PgBinPath       = "C:\Program Files\PostgreSQL\16\bin"   # adjust version - `Get-ChildItem "C:\Program Files\PostgreSQL"`
# =============================================================

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
    & $createdbExe -U $PgSuperuser -h localhost $PgAppDb
    Write-Host "    Database '$PgAppDb' created."
} else {
    Write-Host "    Database '$PgAppDb' already exists, skipping."
}

Write-Host "==> 2/6 Fetching source ($RepoUrl)..."
if (Test-Path $SourcePath) {
    git -C $SourcePath pull
} else {
    git clone $RepoUrl $SourcePath
}

Write-Host "==> 3/6 Publishing the API (Release)..."
New-Item -ItemType Directory -Force -Path $PublishPath | Out-Null
dotnet publish $ProjectPath -c Release -o $PublishPath

Write-Host "==> 4/6 Writing connection string, JWT key and environment into web.config..."
$connString = "Host=localhost;Port=5432;Database=$PgAppDb;Username=$PgSuperuser;Password=$PgSuperPassword"

$bytes = New-Object byte[] 32
(New-Object Security.Cryptography.RNGCryptoServiceProvider).GetBytes($bytes)
$JwtKey = [Convert]::ToBase64String($bytes)

$webConfigPath = Join-Path $PublishPath "web.config"
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

Write-Host "    JWT signing key generated for this deploy:"
Write-Host "    $JwtKey"
Write-Host "    (write this down - tokens issued now won't validate later if you redeploy with a different key)"

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
    $env:ConnectionStrings__DefaultConnection = $connString
    dotnet ef database update
} finally {
    Pop-Location
}

Restart-WebAppPool -Name $AppPoolName

Write-Host ""
Write-Host "Done. API is live under: http://<server-or-domain>/$AppName/api/..."
Write-Host "ASPNETCORE_ENVIRONMENT is 'Production', so /scalar/v1 and /openapi/v1.json are NOT mapped there on purpose"
Write-Host "(they're dev-only in Program.cs). Change that environment variable in web.config to 'Development' if you"
Write-Host "want Scalar reachable there too, e.g. for demoing to the city - just be aware it also re-enables the"
Write-Host "auto-seeder in DbSeeder.cs, which no-ops once any device row exists, so it's harmless after the first run."
Write-Host ""
Write-Host "To load the demo/test dataset, run seed-demo-data.ps1 next."
