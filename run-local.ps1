# ---------------------------------------------------------------------------
#  School Admission System — run everything WITHOUT Docker (Windows, PowerShell)
#
#  Prerequisites on this machine:
#    * .NET 8 SDK        https://dotnet.microsoft.com/download/dotnet/8.0
#    * Node.js 18+       https://nodejs.org
#    * PostgreSQL 16+    https://www.postgresql.org/download/windows/  (psql on PATH)
#
#  Usage (from this folder):
#    powershell -ExecutionPolicy Bypass -File .\run-local.ps1
#    powershell -ExecutionPolicy Bypass -File .\run-local.ps1 -Stop
#
#  Optional parameters / environment variables: -DbHost -DbPort -DbName -DbUser
#  -DbPassword -BackendPort -FrontendPort -HoldDurationSeconds
#
#  Docker is NOT used here. docker-compose.yml remains the graded way to run the app.
# ---------------------------------------------------------------------------
[CmdletBinding()]
param(
    [switch]$Stop,
    [string]$DbHost = $env:DB_HOST,
    [int]$DbPort = $(if ($env:DB_PORT) { [int]$env:DB_PORT } else { 5432 }),
    [string]$DbName = $env:DB_NAME,
    [string]$DbUser = $env:DB_USER,
    [string]$DbPassword = $env:DB_PASSWORD,
    [int]$BackendPort = $(if ($env:BACKEND_PORT) { [int]$env:BACKEND_PORT } else { 8080 }),
    [int]$FrontendPort = $(if ($env:FRONTEND_PORT) { [int]$env:FRONTEND_PORT } else { 5173 }),
    [int]$HoldDurationSeconds = $(if ($env:HOLD_DURATION_SECONDS) { [int]$env:HOLD_DURATION_SECONDS } else { 600 })
)

$ErrorActionPreference = 'Stop'
if (-not $DbHost)     { $DbHost = '127.0.0.1' }
if (-not $DbName)     { $DbName = 'school_admission' }
if (-not $DbUser)     { $DbUser = 'school' }
if (-not $DbPassword) { $DbPassword = 'school' }

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$runDir = Join-Path $root '.run'
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

function Info($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host " ok  $m" -ForegroundColor Green }
function Warn($m) { Write-Host "warn $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "fail $m" -ForegroundColor Red; exit 1 }

function Stop-Previous {
    foreach ($name in 'backend', 'frontend') {
        $pidFile = Join-Path $runDir "$name.pid"
        if (Test-Path $pidFile) {
            $procId = Get-Content $pidFile -ErrorAction SilentlyContinue
            if ($procId) {
                Info "Stopping previous $name (pid $procId)"
                Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
                # Vite/dotnet spawn children; kill the whole tree too.
                Get-CimInstance Win32_Process -Filter "ParentProcessId=$procId" -ErrorAction SilentlyContinue |
                    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
            }
            Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($Stop) { Stop-Previous; Ok 'Stopped.'; exit 0 }

# ---------------------------------------------------------------- tool checks
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { Fail '.NET 8 SDK not found. Install it: https://dotnet.microsoft.com/download/dotnet/8.0' }
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { Fail 'Node.js / npm not found. Install it: https://nodejs.org' }
if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    Fail "PostgreSQL client (psql) not found. Install PostgreSQL 16+ and add its bin folder to PATH, or use Docker: docker compose up --build"
}
Ok "dotnet $(& $dotnet --version) / node $(& node --version)"

# ------------------------------------------------------------------- database
$env:PGPASSWORD = $DbPassword
$psql = (Get-Command psql).Source

function Test-Db([string]$database) {
    & $psql -h $DbHost -p $DbPort -U $DbUser -d $database -tAc 'select 1' 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

if (-not (Test-Db 'postgres') -and -not (Test-Db $DbName)) {
    Warn "Cannot log in to PostgreSQL as '$DbUser'. Make sure the service is running and the role exists:"
    Write-Host "     psql -U postgres -c ""CREATE ROLE $DbUser LOGIN PASSWORD '$DbPassword' CREATEDB;""" -ForegroundColor DarkGray
    Fail "PostgreSQL is not reachable on ${DbHost}:${DbPort} for user '$DbUser'."
}
Ok "Connected to PostgreSQL as '$DbUser'"

if (-not (Test-Db $DbName)) {
    Info "Creating database '$DbName'"
    & $psql -h $DbHost -p $DbPort -U $DbUser -d postgres -c "CREATE DATABASE `"$DbName`";" | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "Could not create database '$DbName'. Create it manually and re-run." }
    Ok "Database '$DbName' created"
}

$connectionString = "Host=$DbHost;Port=$DbPort;Database=$DbName;Username=$DbUser;Password=$DbPassword"

# -------------------------------------------------------------------- install
Info 'Restoring .NET packages'
Push-Location (Join-Path $root 'backend')
& $dotnet restore 'src/SchoolAdmission.Api/SchoolAdmission.Api.csproj' | Out-Null
Pop-Location
Ok 'Backend packages restored'

if (-not (Test-Path (Join-Path $root 'frontend/node_modules'))) {
    Info 'Installing frontend dependencies (first run only)'
    Push-Location (Join-Path $root 'frontend')
    & npm install --no-audit --no-fund | Out-Null
    Pop-Location
}
Ok 'Frontend dependencies present'

# -------------------------------------------------------------------- backend
Info "Starting the API on http://localhost:$BackendPort"
$backendLog = Join-Path $runDir 'backend.log'
$env:ConnectionStrings__Default = $connectionString
$env:HOLD_DURATION_SECONDS = "$HoldDurationSeconds"
$env:ASPNETCORE_URLS = "http://0.0.0.0:$BackendPort"
$env:ASPNETCORE_ENVIRONMENT = 'Development'

$backend = Start-Process -FilePath $dotnet `
    -ArgumentList 'run', '--no-launch-profile', '--project', 'src/SchoolAdmission.Api/SchoolAdmission.Api.csproj' `
    -WorkingDirectory (Join-Path $root 'backend') `
    -RedirectStandardOutput $backendLog -RedirectStandardError (Join-Path $runDir 'backend.err.log') `
    -PassThru -WindowStyle Hidden
$backend.Id | Set-Content (Join-Path $runDir 'backend.pid')

$healthy = $false
for ($i = 0; $i -lt 90; $i++) {
    try {
        Invoke-RestMethod "http://localhost:$BackendPort/health" -TimeoutSec 2 | Out-Null
        $healthy = $true; break
    } catch { Start-Sleep -Seconds 1 }
}
if (-not $healthy) { Warn "API log: $backendLog"; Fail 'The API did not become healthy in 90s.' }
Ok "API healthy — hold window ${HoldDurationSeconds}s"

# ------------------------------------------------------------------- frontend
Info "Starting the UI on http://localhost:$FrontendPort"
$frontendLog = Join-Path $runDir 'frontend.log'
$env:VITE_PROXY_TARGET = "http://localhost:$BackendPort"

$frontend = Start-Process -FilePath 'cmd.exe' `
    -ArgumentList '/c', "npm run dev -- --host 0.0.0.0 --port $FrontendPort --strictPort" `
    -WorkingDirectory (Join-Path $root 'frontend') `
    -RedirectStandardOutput $frontendLog -RedirectStandardError (Join-Path $runDir 'frontend.err.log') `
    -PassThru -WindowStyle Hidden
$frontend.Id | Set-Content (Join-Path $runDir 'frontend.pid')

$uiUp = $false
for ($i = 0; $i -lt 60; $i++) {
    try { Invoke-WebRequest "http://localhost:$FrontendPort/" -TimeoutSec 2 -UseBasicParsing | Out-Null; $uiUp = $true; break }
    catch { Start-Sleep -Seconds 1 }
}
if (-not $uiUp) { Warn "UI log: $frontendLog"; Fail 'The UI did not start.' }
Ok 'UI is up'

Write-Host ''
Write-Host 'School Admission System is running (no Docker)' -ForegroundColor White
Write-Host ''
Write-Host "  UI        http://localhost:$FrontendPort"
Write-Host "  API       http://localhost:$BackendPort   (health: /health)"
Write-Host "  Database  postgres://${DbUser}@${DbHost}:${DbPort}/$DbName"
Write-Host ''
Write-Host "  Logs      $backendLog  ·  $frontendLog"
Write-Host '  Stop      .\run-local.ps1 -Stop'
Write-Host ''
Write-Host 'Open the UI, create a class, type any 17-digit number and apply for a seat.'
