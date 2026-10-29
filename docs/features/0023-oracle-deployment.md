# 0023 — Hosted deployment: Oracle Always Free VM, tailnet-only

> **Status: deferred (2026-10-21).** Oracle's signup rejected the account, so Roamint stays
> local for now and Telegram capture (0024) uses the Cloudflare relay. When hosting is
> picked up again the target is **Hetzner** (CX22-class, German DC — latency from Yerevan
> is on par with Frankfurt). Everything below except the Oracle-specific account/VM steps
> carries over unchanged. The gaps found in the earlier attempt still stand and must be
> fixed then: no authentication (hence tailnet-only), Development-only migrations, and
> `RUNNING_BEHIND_PROXY` set in the `Dockerfile` but never read.

## Context

Roamint runs on the user's PC. When the PC is off (weekends, trips), every capture channel is
down — receipts, manual entry, summary — and the planned Telegram bot (0024) would need a
cloud relay to bridge the gap. Hosting the app on an always-on VM removes the gap for all
channels at once and turns 0024 into a plain webhook.

An earlier, unfinished attempt left a `Dockerfile`, `.dockerignore` and
`deploy/.env.example` that assumed a **public** site (Caddy + DuckDNS + Let's Encrypt).
That premise is wrong for this app: **Roamint has no authentication**. It is safe today only
because it is reachable solely from the LAN/tailnet. This doc keeps that property.

Target: **Oracle Cloud Always Free, Ampere A1 (ARM64)**. Everything here is plain Docker +
Tailscale on Ubuntu, so Hetzner (x86-64, ~€4/mo) is a drop-in fallback if Oracle has no A1
capacity or reclaims the instance.

## Decisions

- **Tailnet-only, no public ingress.** The VM opens **no** inbound ports to the internet
  beyond SSH (and SSH can move to Tailscale-only once it works). The app is served by
  `tailscale serve` on the VM — the same mechanism as 0018 — so phone and PC reach it at
  `https://<vm>.<tailnet>.ts.net` with a real certificate. No login has to be built before
  deploying. **Caddy, DuckDNS and `ROAMINT_DOMAIN` are dropped.**
- **Forward-compatible with the Telegram webhook (0024).** When the bot lands, *one* path
  (`/hooks/telegram`) is exposed publicly with **Tailscale Funnel on port 8443** (a port
  Telegram accepts for webhooks), while the UI stays tailnet-only on 443. Funnel is enabled
  per port, which is exactly why the two live on different ports. Nothing to build now.
- **Docker Compose with two services**: `db` (`pgvector/pgvector:pg17`, same image as the
  AppHost) and `web` (the existing multi-stage `Dockerfile`, **built on the VM** so the image
  matches ARM64). `web` publishes `127.0.0.1:8080` only — reachable by `tailscale serve` on
  the host, not by the internet even if a firewall rule is wrong. `db` publishes nothing.
- **Tailscale on the host, not as a sidecar container.** One `apt install`, `tailscale up`,
  `tailscale serve --bg --https=443 http://127.0.0.1:8080`. A sidecar adds networking
  indirection with no benefit for one app.
- **Migrate on startup in every environment.** The three `*MigrationService`s are
  Development-only today (0005 deferred the production strategy). For a single instance with
  a single user, startup `MigrateAsync` is the correct tool — a migration bundle adds a build
  artifact and a deploy step that buy nothing without concurrent instances. Hosted services
  start before Kestrel listens, so the app never serves against an unmigrated schema. The
  `ReceiptStartupSweep` (0019 crash recovery) is likewise enabled outside Development — a
  crash mid-OCR on the VM needs it more than a dev box does.
- **Honour forwarded headers behind the proxy.** `tailscale serve` terminates TLS and
  forwards plain HTTP with `X-Forwarded-Proto: https`. `Program.cs` already does this in
  Development (0018); the same block applies when `RUNNING_BEHIND_PROXY=true` (the
  `Dockerfile` already sets it, but nothing reads it yet). Without it `UseHttpsRedirection`
  sees `http` and redirects forever. Known-proxy restriction stays off: the only path in is
  the loopback-bound port.
- **Persist Data Protection keys** by mounting a volume at the runtime user's default key
  directory (`/home/app/.aspnet/DataProtection-Keys`, pre-created and chowned in the
  `Dockerfile`). Zero code; otherwise every redeploy invalidates antiforgery tokens and
  logs a warning.
- **Receipt images on a named volume** (`/data/receipts`, already prepared in the
  `Dockerfile`).
- **Embeddings stay off on the VM.** No Ollama container; `RecordItemMapping` already
  degrades to a null vector and nothing reads embeddings since 0021. When Insights (V2)
  needs them: Ollama + bge-m3 in a third container (the A1 has 24 GB RAM) plus a backfill of
  null rows. Not now.
- **Gemini OCR unchanged** — cloud API, key via `Ocr__ApiKey` in `deploy/.env`.
- **Backups: nightly `pg_dump` on the VM + pull to the PC.** A cron job writes a
  custom-format dump and a tarball of receipt images to `/var/backups/roamint`, keeping 14
  days. `scripts/pull-backup.ps1` copies the latest over Tailscale (`scp`) whenever the PC
  is on. Two copies on two machines; restore is rehearsed once during migration (below),
  which is the real test of the backup.
- **Initial data migration = the restore rehearsal.** Dump the local Postgres
  (`pg_dump -Fc roamintdb`), restore it on the VM *before* the first app start. The dump
  carries each schema's `__EFMigrationsHistory`, so startup migration is a no-op. Receipt
  images are copied into the volume the same way.
- **Deploy = `git pull && docker compose up -d --build`** on the VM. The repo is public, so
  the VM clones over HTTPS with no credentials. No CI deploy, no registry — single user.
- **Oracle account: upgrade to Pay-As-You-Go** (still free for Always Free shapes). Idle
  Always Free instances on free-tier accounts can be reclaimed, and PAYG also improves A1
  capacity availability. Set a budget alert at $1.

## Infrastructure (`deploy/`)

- `deploy/compose.yaml` — `db` + `web`, named volumes `pgdata`, `receipts`, `dpkeys`,
  `restart: unless-stopped`, `db` healthcheck (`pg_isready`) gating `web`.
- `deploy/.env.example` — `POSTGRES_PASSWORD`, `Ocr__ApiKey` (drop `ROAMINT_DOMAIN`).
- `deploy/backup.sh` + a cron line; `scripts/pull-backup.ps1` on the PC side.
- `deploy/README.md` — the runbook: VM creation → hardening → Docker/Tailscale → data
  restore → first start → backups → redeploy.

## Code changes

- `ExpensesMigrationService`, `WalletMigrationService`, `ReceiptsMigrationService`: remove
  the Development-only guard.
- `ReceiptStartupSweep`: remove the Development-only guard.
- `Program.cs`: apply the forwarded-headers block when Development **or**
  `RUNNING_BEHIND_PROXY=true`.
- `Dockerfile`: pre-create the Data Protection key directory; fix the doc reference.

## Public surface

None — no module API changes.

## Open questions

- **SSH over Tailscale only?** Closing port 22 publicly after the first boot is better
  hygiene; the risk is locking yourself out if Tailscale breaks. Oracle's serial console is
  the escape hatch. Recommended once everything works.
- **Local dev after the move.** The PC keeps its own Aspire Postgres for development; the VM
  is the source of truth for real data. Pulling a fresh copy down for testing = restoring a
  backup locally (no tooling until it's needed).
- **Phone app URL changes** to the VM's `ts.net` name; the PWA (0020) must be re-installed
  from the new origin.

## Verification

- **Local build:** `docker build .` succeeds on the PC (x86-64) — proves the Dockerfile; the
  VM build proves ARM64.
- **Compose locally:** `docker compose -f deploy/compose.yaml up` against an empty volume →
  app starts, all three schemas migrate, `/` renders at `http://127.0.0.1:8080`.
- **On the VM:** restore the dump → start → `https://<vm>.ts.net` from the phone shows the
  real monthly summary; add an expense; snap a receipt (OCR via Gemini works); restart the
  `web` container → data, images and the session survive; a device *off* the tailnet cannot
  reach the app (port scan of the public IP shows only 22).
- **Backup:** run `backup.sh` by hand, pull it with `pull-backup.ps1`, restore it into a
  scratch local Postgres and see the same expense count.
