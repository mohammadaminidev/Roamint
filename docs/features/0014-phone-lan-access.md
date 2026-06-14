# 0014 — Phone / LAN access to the dev app

## Context

The app is used daily from a phone (expense capture on the go, and the planned
receipt-upload flow). The phone reaches the laptop over a **mobile hotspot**: the
laptop joins the phone's hotspot, so the laptop sits on the phone's subnet (e.g.
`10.252.153.0/24`) and the phone reaches the laptop at the laptop's address on
that network.

The normal `dotnet run` against the Aspire AppHost does **not** support this:

- Aspire's inner-loop **proxy binds the published endpoints to `localhost`**, so
  nothing on the LAN can reach them. There is a standing Aspire issue
  ([dotnet/aspire#3146](https://github.com/dotnet/aspire/issues/3146)) confirming
  `WithLaunchProfile` + `0.0.0.0` does not reliably expose a project through the
  AppHost proxy.
- Aspire **reassigns the app's internal HTTP port on every restart**, so any
  hand-rolled `netsh portproxy` bridge points at a moving target and breaks.
- The Blazor template's unconditional `app.UseHttpsRedirection()` 307-redirects
  plain HTTP to the HTTPS port, which is itself bound to `localhost` and serves a
  dev cert the phone does not trust.

## Decisions

- **Bypass the Aspire proxy for phone sessions; run `Roamint.Web` standalone**,
  bound to `http://0.0.0.0:5180`, in the `Development` environment. This is the
  only reliable way to bind all interfaces; the Aspire stack stays the primary
  inner-loop tool for localhost work.
- **No HTTPS redirect in Development.** `UseHttpsRedirection`/`UseHsts` now run
  only outside Development (see `Roamint.Web/Program.cs`). Phone access is plain
  HTTP on the LAN. (Live camera capture will later need HTTPS + a trusted cert —
  tracked separately, out of scope here. Photo file-upload works over HTTP.)
- **Postgres becomes a stable, shared dependency** instead of an ephemeral
  per-run container:
  - **Fixed host port** via `WithHostPort(15432)` so the standalone connection
    string is deterministic.
  - **`ContainerLifetime.Persistent`** so the container survives AppHost
    shutdown — phone sessions can run `scripts/phone.ps1` **without** starting
    Aspire at all.
  - The **password was already stable**: Aspire persists its generated
    `Parameters:postgres-password` in AppHost user-secrets and reuses it across
    runs. The existing `roamint-pgdata` volume keeps its data and password.
- **Standalone connection string lives in `Roamint.Web` user-secrets**
  (`ConnectionStrings:roamintdb`), not in committed config. ASP.NET auto-loads
  user-secrets in Development, so the standalone run picks it up. When launched
  via Aspire, Aspire's injected env var takes precedence (env > user-secrets), so
  the two run modes don't conflict and no secret is committed.
- **`scripts/phone.ps1`** is the one-command entry point: ensure the Postgres
  container is up (`docker start`), build, print the LAN URL, and run the host on
  `0.0.0.0:5180`.

## Public surface

No application/domain surface. Operational only:

- `scripts/phone.ps1` — launch the phone-facing host.
- AppHost now publishes Postgres on a fixed `localhost:15432` (dev only).
- `Roamint.Web` gains a `UserSecretsId`.

## One-time machine setup (not in source control)

1. **Firewall** — allow inbound TCP 5180 (already added this session):
   `New-NetFirewallRule -DisplayName "Roamint dev 5180" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5180`
2. **Remove the throwaway portproxy** from earlier debugging (elevated), so the
   standalone host can bind `0.0.0.0:5180`:
   `netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=5180`
3. **Web connection string** in user-secrets (machine-local):
   `dotnet user-secrets --project src/Bootstrap/Roamint.Web set "ConnectionStrings:roamintdb" "Host=127.0.0.1;Port=15432;Username=postgres;Password=<apphost postgres-password>;Database=roamintdb"`

## Open questions

- HTTPS for the phone (required for live camera capture in the Receipts flow):
  mkcert-style locally-trusted cert vs. a tunnel. Deferred until Receipts V2.
- Whether to fold the standalone launch into Aspire once the proxy gains a
  documented all-interfaces binding.

## Verification

- From the laptop, with the host running: `curl http://10.252.153.225:5180`
  returns **HTTP 200** (not 307, not empty).
- From the phone on the hotspot: `http://<laptop-LAN-IP>:5180` loads the reports
  home and interactive entry works (Blazor Server circuit over the same port).
- Restart the laptop / AppHost and confirm Postgres is still reachable at
  `localhost:15432` with the same data (persistent container + volume).
