# 0018 — Tailscale HTTPS + live camera with token-capped capture

## Context

Two linked goals, both serving the daily receipt-capture flow (0015):

1. **A trusted HTTPS origin for the phone**, so the in-page live-camera viewfinder
   (`getUserMedia`) works. Live camera needs a *secure context*; the dev cert is
   untrusted on the phone, so 0014/0015 deferred it and used the native file-input
   `capture="environment"` (which works over plain HTTP). Tailscale Serve mints a real
   Let's Encrypt cert for the laptop's `*.ts.net` name, giving a trusted secure context
   with no cert-trust dance — and as a bonus, access from **anywhere**, not just the
   hotspot subnet.
2. **Cap the captured image size to save Gemini tokens.** Today the phone uploads a
   full-resolution JPEG (~3000×4000, 4–12 MB) and it goes to Gemini **raw**
   (`Ocr.cs` → `DataContent(image, contentType)`). Gemini 2.5 Flash bills images by
   **pixel dimensions** (~258 tokens per 768×768 tile), so a full photo ≈ 24 tiles
   ≈ 6.2k tokens; capped to a ~1600px long edge ≈ 6 tiles ≈ 1.5k tokens — roughly a 4×
   cut with no meaningful accuracy loss on a single-column receipt. JPEG re-compression
   alone does **not** help (tokens track dimensions, not bytes); the **scale-down** is
   the lever.

The live camera is also the natural place to apply the cap: a captured video frame is
drawn into a `<canvas>` at a controlled resolution, so the downscale is free.

### What this is *not*

- **Not** a new bounded context or any change to `src/Modules`. This is web-host UI
  (`Roamint.Web`) plus an ops script. The Receipts upload/OCR/match pipeline is unchanged
  — the same `IReceiptsApi.UploadAsync(bytes, contentType)` is called; only the
  **capture front-end** changes and the bytes it hands over are smaller.
- **Not** a server-side resize. No SkiaSharp/ImageSharp dependency. The client always
  produces a capped image; a native-decode defense-in-depth cap would be gold-plating.
  Revisit only if a non-JS upload path is ever added.
- **Not** removing the LAN/hotspot path (0014). `scripts/phone.ps1` stays. Tailscale is
  an additional, preferred entry that also covers away-from-home use.

## Decisions

- **Tailscale Serve terminates TLS; the app stays HTTP.** `tailscale serve --bg
  --https=443 http://localhost:5180` reverse-proxies the standalone host. No dev cert, no
  HTTPS redirect inside the app. The phone reaches `https://<machine>.<tailnet>.ts.net`.
  One-time, out-of-band setup (install on laptop + phone, same tailnet, enable MagicDNS +
  HTTPS certificates in the admin console) is documented in this file, **not** in source
  control — same posture as the 0014 firewall/user-secrets setup.
- **`ForwardedHeaders` middleware, dev-only.** Tailscale forwards plain HTTP with
  `X-Forwarded-Proto: https`. The Blazor Server circuit (WebSocket) and any
  scheme-sensitive logic should see the original `https` scheme. Add
  `app.UseForwardedHeaders(...)` honouring `XForwardedProto`/`XForwardedHost`, gated to
  Development (the only environment this script runs in). Known-proxies/networks cleared
  because the proxy is loopback on the same host.
- **Two separate capture affordances, not a fallback chain.** `/receipts/new` shows a
  **"Use camera"** button (live in-page viewfinder) *and* a **"Choose photo"** native
  file input — distinct controls, user picks. Live camera only functions in a secure
  context; the file input always works (and over plain HTTP). Both funnel through the
  **same** client-side downscale, so either path saves tokens.
- **Downscale in the browser via canvas.** A single ES module
  `wwwroot/js/receipt-camera.js` owns both: the live-camera lifecycle *and* a
  `resizeBlobToBytes` used by both the camera capture and the file selection. Cap: **long
  edge ≤ 1600 px, JPEG quality 0.85** — exported as module constants so they're trivial to
  tune if a thermal receipt's digits ever blur. Output is always `image/jpeg`.
- **Bytes cross the interop boundary as a `DotNetStreamReference`/byte[]**, not base64 —
  avoids a ~33% base64 inflation on the wire of an already-capped image. The Blazor page
  receives the bytes and calls `UploadAsync(bytes, "image/jpeg")` exactly as the
  file-input path does today.
- **No Razor-page resize on the native path beyond the JS step.** The existing
  `OpenReadStream(MaxBytes)` guard stays as the accidental-huge-file backstop; the JS
  resize happens first, client-side, before bytes reach .NET.

## Public surface

None. No new DTOs, ports, or Contracts changes. `IReceiptsApi.UploadAsync` is unchanged.

## Web UI (`Roamint.Web`)

- `wwwroot/js/receipt-camera.js` (ES module, loaded via `IJSRuntime.InvokeAsync`):
  - `start(video)` → `getUserMedia({ video: { facingMode: { ideal: 'environment' } } })`,
    attach stream to the `<video>` element, return whether it succeeded (so the page can
    show a "camera unavailable" hint when not in a secure context).
  - `capture(video)` → draw the current frame to an offscreen canvas scaled so the long
    edge ≤ `MAX_EDGE`, `toBlob('image/jpeg', QUALITY)`, return bytes via
    `DotNetStreamReference`.
  - `resizeFile(file)` → same canvas pipeline for a `File` chosen via the native input.
  - `stop()` → stop all tracks (called on capture and on page dispose).
- `ReceiptCapture.razor`: add the live-camera block (`<video>` preview, "Use camera" /
  "Capture" / "Cancel" controls) alongside the existing file `<label>`. The `OnFile`
  handler routes the selected file through `resizeFile` before `UploadAsync`. `IJSRuntime`
  injected; module imported lazily on first use; `IAsyncDisposable` to `stop()` the stream.
  The post-upload draft→match→handoff logic (0016) is untouched.
- `ReceiptCapture.razor.css`: styles for the viewfinder and the new buttons.

## Ops (`scripts/`)

- `scripts/phone-https.ps1`: ensures Postgres is up (mirrors `phone.ps1`), builds + runs
  the host on `http://localhost:5180` (Development), and runs `tailscale serve` to expose
  it as HTTPS. Prints the `https://<machine>.ts.net` URL. Documents prerequisites in its
  help block.

## One-time setup (not in source control)

1. Install Tailscale on the laptop and phone; sign both into the same tailnet.
2. Admin console: enable **MagicDNS** and **HTTPS Certificates** for the tailnet.
3. (First run) `tailscale cert` provisions the cert; `tailscale serve` uses it.
4. Phone opens `https://<machine>.<tailnet>.ts.net` → trusted → live camera allowed.

## Open questions

- Exact `MAX_EDGE` sweet spot for thermal receipts — start at 1600; lower only if uploads
  feel heavy, raise if Gemini starts missing small digits. Tunable constant.
- iOS Safari `getUserMedia` quirks (autoplay/`playsinline`, permission persistence) — the
  `<video>` needs `playsinline muted autoplay`; verify on the actual phone.
- Whether to persist the chosen entry (camera vs file) — not worth it for one user.

## Verification

- **Token cap:** capture a receipt; confirm the uploaded JPEG is ≤ ~1600 px long edge
  (log byte length / inspect stored image) and that Gemini still extracts lines correctly.
- **Live camera (manual, phone over Tailscale):** open
  `https://<machine>.ts.net/receipts/new` → "Use camera" shows the viewfinder → capture →
  review screen pre-filled → confirm → expense recorded, receipt linked (the 0015 e2e
  path, now via live camera).
- **File path still works:** "Choose photo" → resized → same review flow, including over
  plain HTTP via `scripts/phone.ps1` (no secure context; camera button shows unavailable).
- **Forwarded headers:** Blazor circuit connects over `wss://…ts.net` without
  mixed-content or scheme errors in the phone's console.
