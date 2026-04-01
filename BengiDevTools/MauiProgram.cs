using CommunityToolkit.Maui;
using BengiDevTools.Services;
using BengiDevTools.ViewModels;
using BengiDevTools.Views;

namespace BengiDevTools;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts => { });

        // Services
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IBuildService, BuildService>();
        builder.Services.AddSingleton<IProcessService, ProcessService>();
        builder.Services.AddSingleton<IGitService, GitService>();

        // ViewModels
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<BuildViewModel>();
        builder.Services.AddSingleton<AppsViewModel>();

        // Pages
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<BuildPage>();
        builder.Services.AddSingleton<AppsPage>();

        var app = builder.Build();

        // Load settings on startup
        app.Services.GetRequiredService<ISettingsService>().Load();

        return app;
    }
}
