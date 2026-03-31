using CommunityToolkit.Mvvm.ComponentModel;

namespace BengiDevTools.Models;

public partial class RepoBuildTarget : ObservableObject
{
    public required string RepoName { get; init; }
    public required string SlnPath { get; init; }

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _status = "";
}
