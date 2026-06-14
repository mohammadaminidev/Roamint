using Roamint.Expenses.Contracts;
using Roamint.Wallet.Contracts;
using Roamint.Web.Components;
using Roamint.Web.Import;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddExpensesModule();
builder.AddWalletModule();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Dev-only spreadsheet import tooling (0013); the /import page guards on the environment.
builder.Services.AddScoped<SpreadsheetImporter>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
