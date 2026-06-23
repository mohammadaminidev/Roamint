<#
.SYNOPSIS
    Run Roamint.Web behind Tailscale Serve so the phone reaches it over trusted HTTPS,
    enabling the live in-page camera (getUserMedia needs a secure context).

.DESCRIPTION
    Live-camera capture (doc 0018) requires a trusted HTTPS origin; the dev cert is
    untrusted on the phone. Tailscale Serve terminates TLS with a real Let's Encrypt cert
    for the laptop's *.ts.net name and reverse-proxies to the app on plain HTTP, so no dev
    cert is involved and the app stays HTTP. Unlike scripts/phone.ps1 (hotspot LAN), this
    also works away from home, as long as both devices are on the tailnet.

    The app runs on http://localhost:$Port (Development; ForwardedHeaders honours the
    X-Forwarded-Proto from the proxy — see Program.cs). The connection string comes from
    Roamint.Web user-secrets (ConnectionStrings:roamintdb), same as phone.ps1.

    ONE-TIME SETUP (not in source control):
      1. Install Tailscale on the laptop and phone; sign both into the same tailnet.
      2. Admin console: enable MagicDNS and HTTPS Certificates for the tailnet.
      3. First run provisions the cert automatically via `tailscale serve`.
    Phone then opens https://<machine>.<tailnet>.ts.net/receipts/new.

.PARAMETER Port
    Local host port the app binds and Tailscale proxies to. Defaults to 5180.
#>
[CmdletBinding()]
param(
    [int]$Port = 5180
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$webProject = Join-Path $repoRoot 'src/Bootstrap/Roamint.Web'

# 0. Tailscale must be installed and up.
$tailscale = Get-Command tailscale -ErrorAction SilentlyContinue
if (-not $tailscale) {
    Write-Warning "Tailscale CLI not found on PATH. Install Tailscale and sign in, then re-run."
    Write-Warning "See the ONE-TIME SETUP block in this script's help (Get-Help ./scripts/phone-https.ps1)."
    exit 1
}

# 1. Ensure the persistent Postgres container is running (mirrors phone.ps1).
$pg = docker ps --filter 'ancestor=pgvector/pgvector:pg17' --format '{{.ID}}' | Select-Object -First 1
if (-not $pg) {
    $stopped = docker ps -a --filter 'ancestor=pgvector/pgvector:pg17' --format '{{.ID}}' | Select-Object -First 1
    if ($stopped) {
        Write-Host "Starting Postgres container $stopped ..." -ForegroundColor Cyan
        docker start $stopped | Out-Null
        $pg = $stopped
    }
    else {
        Write-Warning "No Postgres container found. Run the Aspire AppHost once to create it:"
        Write-Warning "  dotnet run --project src/Hosting/Roamint.AppHost --launch-profile http"
        exit 1
    }
}

# 1b. Derive the live connection string. Aspire's WithHostPort(15432) only pins the DCP
#     proxy, which isn't running in this standalone path — the container itself is published
#     on a RANDOM host port that changes on every recreation. So read the actual port from
#     docker and retarget the user-secret connection string (keeps the password out of the
#     repo) via an env override, which outranks user-secrets in config precedence.
$pgPort = (docker port $pg 5432 | Select-Object -First 1).Split(':')[-1].Trim()
if (-not $pgPort) {
    Write-Warning "Could not read the Postgres published port from docker."
    exit 1
}
$secretLine = dotnet user-secrets list --project $webProject 2>$null |
    Where-Object { $_ -match '^ConnectionStrings:roamintdb\s*=' } | Select-Object -First 1
if (-not $secretLine) {
    Write-Warning "ConnectionStrings:roamintdb not found in Roamint.Web user-secrets (see docs/features/0014)."
    exit 1
}
$env:ConnectionStrings__roamintdb = (($secretLine -split '=', 2)[1].Trim() -replace 'Port=\d+', "Port=$pgPort")
Write-Host "Postgres: 127.0.0.1:$pgPort" -ForegroundColor DarkGray

# 2. Point Tailscale Serve at the local app (HTTPS, default mode). Idempotent.
#    Serve defaults to HTTPS and provisions a Let's Encrypt cert for the *.ts.net name.
#    That needs MagicDNS + "HTTPS Certificates" enabled in the admin console; without it
#    `serve` blocks forever trying to fetch a cert, so we run it with a timeout guard and
#    do NOT swallow its output.
$dns = (tailscale status --json | ConvertFrom-Json).Self.DNSName.TrimEnd('.')
Write-Host "Configuring Tailscale Serve -> http://localhost:$Port ..." -ForegroundColor Cyan

$serveJob = Start-Job { & tailscale serve --bg --https=443 "http://localhost:$using:Port" *>&1 }
if (Wait-Job $serveJob -Timeout 30) {
    Receive-Job $serveJob | ForEach-Object { Write-Host "  $_" }
}
else {
    Stop-Job $serveJob
}
Remove-Job $serveJob -Force

# Verify it actually took effect (a hang/error leaves no serve config).
$serveOk = (tailscale serve status 2>&1) -notmatch 'No serve config'
if (-not $serveOk) {
    Write-Warning "Tailscale Serve did not come up. The usual cause is HTTPS certs not being enabled."
    Write-Warning "Enable MagicDNS + HTTPS Certificates here, then re-run this script:"
    Write-Warning "  https://login.tailscale.com/admin/dns"
    Write-Warning "(Verify with: tailscale cert $dns — it should mint a cert, not 500.)"
    exit 1
}

# 3. Show the HTTPS URL the phone should open.
if ($dns) {
    Write-Host "`nReachable from the phone (on the tailnet) at:" -ForegroundColor Green
    Write-Host "  https://$dns/receipts/new" -ForegroundColor Green
    Write-Host ""
}

# 4. Build, then run standalone on localhost (Tailscale handles TLS + exposure).
dotnet build $webProject -c Debug --nologo
$env:ASPNETCORE_ENVIRONMENT = 'Development'
try {
    dotnet run --project $webProject --no-launch-profile --no-build -c Debug --urls "http://localhost:$Port"
}
finally {
    # Tear down the proxy when the app stops so the *.ts.net name doesn't dangle.
    Write-Host "`nResetting Tailscale Serve ..." -ForegroundColor Cyan
    tailscale serve reset 2>$null | Out-Null
}
