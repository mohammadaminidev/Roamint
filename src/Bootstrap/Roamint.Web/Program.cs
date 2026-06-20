using Roamint.Expenses.Contracts;
using Roamint.Receipts.Contracts;
using Roamint.Wallet.Contracts;
using Roamint.Web.Components;
using Roamint.Web.Import;
using Roamint.Web.Receipts;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddExpensesModule();
builder.AddWalletModule();
builder.AddReceiptsModule();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Dev-only spreadsheet import tooling (0013); the /import page guards on the environment.
builder.Services.AddScoped<SpreadsheetImporter>();

// Carries an OCR'd receipt draft from /receipts/new to /add within the circuit (0015).
builder.Services.AddScoped<ReceiptHandoff>();

var app = builder.Build();

app.MapDefaultEndpoints();

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
