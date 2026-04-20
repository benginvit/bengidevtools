using BengiDevTools.Models;
using BengiDevTools.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ISettingsService,  SettingsService>();
builder.Services.AddSingleton<ITestDataService,  TestDataService>();
builder.Services.AddSingleton<ITestCaseService,  TestCaseService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.Services.GetRequiredService<ISettingsService>().Load();

// ─── Settings ─────────────────────────────────────────────────────────────────

app.MapGet("/api/settings", (ISettingsService s) => s.Settings);

app.MapPut("/api/settings", (AppSettings body, ISettingsService s) =>
{
    s.Settings.SqlConnectionString = body.SqlConnectionString;
    s.Settings.DebugScriptsPath    = body.DebugScriptsPath;
    s.Save();
    return Results.Ok(s.Settings);
});

app.MapRazorComponents<BengiDevTools.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run("http://0.0.0.0:5050");
