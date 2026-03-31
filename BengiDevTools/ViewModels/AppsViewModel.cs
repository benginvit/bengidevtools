using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BengiDevTools.Models;
using BengiDevTools.Services;

namespace BengiDevTools.ViewModels;

public partial class AppsViewModel : ObservableObject
{
    private readonly IProcessService _processService;
    private readonly ISettingsService _settingsService;
    private readonly IGitService _gitService;

    public ObservableCollection<AppGroup> Groups { get; } = new();

    [ObservableProperty]
    public partial bool IsRefreshingGit { get; set; }

    [ObservableProperty]
    public partial int RunningCount { get; set; }

    public AppsViewModel(
        IProcessService processService,
        ISettingsService settingsService,
        IGitService gitService)
    {
        _processService = processService;
        _settingsService = settingsService;
        _gitService = gitService;

        _processService.AppStateChanged += OnAppStateChanged;
        InitializeGroups();
    }

    private void InitializeGroups()
    {
        var grouped = AppRegistry.Apps.GroupBy(a => a.Group);
        foreach (var group in grouped)
        {
            var appGroup = new AppGroup { Name = group.Key };
            foreach (var entry in group)
            {
                appGroup.Apps.Add(new AppDefinition
                {
                    Name = entry.Name,
                    Port = entry.Port,
                    Group = entry.Group,
                    RepoKey = entry.RepoKey,
                    ProjectName = entry.ProjectName,
                });
            }
            Groups.Add(appGroup);
        }
    }

    private void OnAppStateChanged(object? sender, (string appName, bool isRunning) e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var app = Groups
                .SelectMany(g => g.Apps)
                .FirstOrDefault(a => a.Name == e.appName);
            if (app != null)
                app.IsRunning = e.isRunning;
            RunningCount = Groups.SelectMany(g => g.Apps).Count(a => a.IsRunning);
        });
    }

    [RelayCommand]
    private async Task StartApp(AppDefinition app)
    {
        await _processService.StartAsync(app, _settingsService.Settings.RepoRootPath);
        app.IsRunning = _processService.IsRunning(app.Name);
        RunningCount = Groups.SelectMany(g => g.Apps).Count(a => a.IsRunning);
    }

    [RelayCommand]
    private async Task StopApp(AppDefinition app)
    {
        await _processService.StopAsync(app);
        app.IsRunning = false;
        RunningCount = Groups.SelectMany(g => g.Apps).Count(a => a.IsRunning);
    }

    [RelayCommand]
    private async Task RestartApp(AppDefinition app)
    {
        await _processService.RestartAsync(app, _settingsService.Settings.RepoRootPath);
        app.IsRunning = _processService.IsRunning(app.Name);
    }

    [RelayCommand]
    private async Task StartAll()
    {
        var root = _settingsService.Settings.RepoRootPath;
        foreach (var app in Groups.SelectMany(g => g.Apps))
            await _processService.StartAsync(app, root);
    }

    [RelayCommand]
    private async Task StopAll()
    {
        foreach (var app in Groups.SelectMany(g => g.Apps).Where(a => a.IsRunning))
            await _processService.StopAsync(app);
    }

    [RelayCommand]
    private async Task RefreshGitStatus()
    {
        IsRefreshingGit = true;
        try
        {
            var root = _settingsService.Settings.RepoRootPath;
            var sem = new SemaphoreSlim(4);
            var tasks = Groups
                .SelectMany(g => g.Apps)
                .Select(async app =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        if (!AppRegistry.RepoMap.TryGetValue(app.RepoKey, out var repoFolder))
                            return;
                        var repoPath = Path.Combine(root, repoFolder);
                        app.GitStatus = await _gitService.GetStatusAsync(repoPath);
                    }
                    finally
                    {
                        sem.Release();
                    }
                });
            await Task.WhenAll(tasks);
        }
        finally
        {
            IsRefreshingGit = false;
        }
    }
}
