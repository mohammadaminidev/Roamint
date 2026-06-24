using Microsoft.AspNetCore.HttpOverrides;
using Roamint.Expenses.Contracts;
using Roamint.Receipts.Contracts;
using Roamint.Wallet.Contracts;
using Roamint.Web.Capture;
using Roamint.Web.Components;
using Roamint.Web.Import;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddExpensesModule();
builder.AddWalletModule();
builder.AddReceiptsModule();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        // Live-camera capture (0018) returns the resized JPEG bytes to .NET over the circuit,
        // which arrives as a single SignalR message. The default receive cap is 32 KB — far
        // too small for an image — so the capture silently fails without this. A capped
        // ~1600px JPEG is well under a few hundred KB; 16 MB is generous headroom and a
        // non-issue for a single-user app on a private tailnet.
        options.MaximumReceiveMessageSize = 16L * 1024 * 1024;
    });

// Dev-only spreadsheet import tooling (0013); the /import page guards on the environment.
builder.Services.AddScoped<SpreadsheetImporter>();

// Carries a parsed capture (scanned receipt 0015, pasted order 0017) from its source page to /add within
// the circuit.
builder.Services.AddScoped<CaptureHandoff>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    // Behind Tailscale Serve (scripts/phone-https.ps1, doc 0018) TLS is terminated by the
    // proxy and forwarded as plain HTTP with X-Forwarded-Proto: https. Honour it so the
    // Blazor circuit (wss) and any scheme-sensitive logic see the original https origin.
    // The proxy is loopback on this host, so no known-proxy/network restriction is needed.
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    };
    forwardedOptions.KnownIPNetworks.Clear();
    forwardedOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedOptions);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    // HTTPS redirect only outside dev: the phone-over-hotspot flow reaches the
    // app over plain HTTP on the LAN, and the HTTPS port binds to localhost only.
    app.UseHttpsRedirection();
}

app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
