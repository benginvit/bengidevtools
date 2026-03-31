using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BengiDevTools.Models;
using BengiDevTools.Services;

namespace BengiDevTools.ViewModels;

public partial class BuildViewModel : ObservableObject
{
    private readonly IBuildService _buildService;
    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _cts;

    public ObservableCollection<RepoBuildTarget> BuildTargets { get; } = new();
    public BuildFlags Flags { get; } = new();

    [ObservableProperty]
    private bool _isBuilding;

    [ObservableProperty]
    private string _buildLog = "";

    [ObservableProperty]
    private int _succeededCount;

    [ObservableProperty]
    private int _failedCount;

    public BuildViewModel(IBuildService buildService, ISettingsService settingsService)
    {
        _buildService = buildService;
        _settingsService = settingsService;

        Flags.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BuildFlags.Snabb) && Flags.Snabb)
            {
                Flags.NoRestore = true;
                Flags.NoAnalyzers = true;
                Flags.NoDocs = true;
                Flags.Parallel = true;
            }
        };
    }

    public void DiscoverRepos()
    {
        BuildTargets.Clear();
        var root = _settingsService.Settings.RepoRootPath;

        if (!Directory.Exists(root))
            return;

        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d))
        {
            var slnFiles = Directory.GetFiles(dir, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length == 0)
                slnFiles = Directory.GetFiles(dir, "*.sln", SearchOption.AllDirectories).Take(1).ToArray();

            if (slnFiles.Length > 0)
            {
                BuildTargets.Add(new RepoBuildTarget
                {
                    RepoName = Path.GetFileName(dir),
                    SlnPath = slnFiles[0]
                });
            }
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var t in BuildTargets)
            t.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var t in BuildTargets)
            t.IsSelected = false;
    }

    [RelayCommand]
    private async Task BuildAsync()
    {
        IsBuilding = true;
        BuildLog = "";
        SucceededCount = 0;
        FailedCount = 0;
        _cts = new CancellationTokenSource();

        var selected = BuildTargets.Where(t => t.IsSelected).ToList();
        foreach (var t in selected)
            t.Status = "";

        var progress = new Progress<(string repo, string status)>(p =>
        {
            var target = BuildTargets.FirstOrDefault(t => t.RepoName == p.repo);
            if (target != null)
            {
                target.Status = p.status;
                if (p.status == "OK") SucceededCount++;
                else if (p.status == "FAILED") FailedCount++;
            }
        });

        try
        {
            await _buildService.BuildAsync(
                selected,
                Flags,
                progress,
                line => MainThread.BeginInvokeOnMainThread(() => BuildLog += line + "\n"),
                _cts.Token);
        }
        catch (OperationCanceledException)
        {
            BuildLog += "\n⛔ Bygge avbrutet.\n";
        }
        finally
        {
            IsBuilding = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }
}
