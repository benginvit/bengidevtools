using CommunityToolkit.Mvvm.ComponentModel;

namespace BengiDevTools.Models;

public partial class RepoBuildTarget : ObservableObject
{
    public required string RepoName { get; init; }
    public required string SlnPath { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;

    [ObservableProperty]
    public partial string Status { get; set; } = "";
}
