using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BengiDevTools.Services;

namespace BengiDevTools.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    public partial string RepoRootPath { get; set; } = @"C:\TFS\Repos";

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        RepoRootPath = _settingsService.Settings.RepoRootPath;
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.Settings.RepoRootPath = RepoRootPath;
        _settingsService.Save();
    }
}
