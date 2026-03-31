using CommunityToolkit.Mvvm.ComponentModel;

namespace BengiDevTools.Models;

public partial class AppDefinition : ObservableObject
{
    public required string Name { get; init; }
    public required int Port { get; init; }
    public required string Group { get; init; }
    public required string RepoKey { get; init; }
    public required string ProjectName { get; init; }

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _gitStatus = "–";

    public string LocalhostUrl => $"https://localhost:{Port}";
}
