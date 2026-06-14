<#
.SYNOPSIS
    Run Roamint.Web bound to all interfaces so a phone on the same network
    (e.g. the laptop's mobile-hotspot subnet) can reach it over plain HTTP.

.DESCRIPTION
    Bypasses the Aspire inner-loop proxy (which binds localhost only). Ensures
    the persistent Postgres container is up, builds the web host, and runs it on
    http://0.0.0.0:5180 in the Development environment (no HTTPS redirect).

    The connection string is read from Roamint.Web user-secrets
    (ConnectionStrings:roamintdb). See docs/features/0014-phone-lan-access.md for
    one-time setup (firewall rule, portproxy removal, user-secrets).

.PARAMETER Port
    Host port to bind. Defaults to 5180 (matches the firewall rule).
#>
[CmdletBinding()]
param(
    [int]$Port = 5180
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$webProject = Join-Path $repoRoot 'src/Bootstrap/Roamint.Web'

# 1. Ensure the persistent Postgres container is running.
$pg = docker ps --filter 'ancestor=pgvector/pgvector:pg17' --format '{{.ID}}' | Select-Object -First 1
if (-not $pg) {
    $stopped = docker ps -a --filter 'ancestor=pgvector/pgvector:pg17' --format '{{.ID}}' | Select-Object -First 1
    if ($stopped) {
        Write-Host "Starting Postgres container $stopped ..." -ForegroundColor Cyan
        docker start $stopped | Out-Null
    }
    else {
        Write-Warning "No Postgres container found. Run the Aspire AppHost once to create it:"
        Write-Warning "  dotnet run --project src/Hosting/Roamint.AppHost --launch-profile http"
        exit 1
    }
}

# 2. Show the LAN addresses the phone can use.
Write-Host "`nReachable from the phone at:" -ForegroundColor Green
Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '169.254.*' -and $_.IPAddress -ne '127.0.0.1' -and $_.InterfaceAlias -notlike '*WSL*' } |
    ForEach-Object { Write-Host ("  http://{0}:{1}" -f $_.IPAddress, $Port) -ForegroundColor Green }
Write-Host ""

# 3. Build, then run standalone bound to all interfaces.
dotnet build $webProject -c Debug --nologo
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project $webProject --no-launch-profile --no-build -c Debug --urls "http://0.0.0.0:$Port"
