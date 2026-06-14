using Roamint.Expenses.Contracts;
using Roamint.Wallet.Contracts;
using Roamint.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddExpensesModule();
builder.AddWalletModule();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

// Home redirects to the expense list for now (a reports dashboard will live here later). Done at the HTTP
// level rather than a component, so it never trips NavigationException under interactive (pre)rendering.
app.MapGet("/", () => Results.Redirect("/expenses"));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
