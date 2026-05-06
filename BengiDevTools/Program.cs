using BengiDevTools.Endpoints;
using BengiDevTools.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ISettingsService,  SettingsService>();
builder.Services.AddSingleton<IBuildService,     BuildService>();
builder.Services.AddSingleton<IProcessService,   ProcessService>();
builder.Services.AddSingleton<IGitService,       GitService>();
builder.Services.AddSingleton<ITestDataService,  TestDataService>();
builder.Services.AddSingleton<ITestCaseService,  TestCaseService>();
builder.Services.AddSingleton<AppScanService>();
builder.Services.AddHostedService<GitScanBackgroundService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.WebHost.UseUrls("http://+:5050");

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.Services.GetRequiredService<ISettingsService>().Load();
app.Services.GetRequiredService<AppScanService>().LoadCache();

app.MapSettingsEndpoints();
app.MapAppsEndpoints();
app.MapBuildEndpoints();
app.MapDebugEndpoints();

app.MapRazorComponents<BengiDevTools.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();
