using CommunityToolkit.Mvvm.ComponentModel;

namespace BengiDevTools.Models;

public partial class BuildFlags : ObservableObject
{
    [ObservableProperty]
    private bool _noRestore;

    [ObservableProperty]
    private bool _noAnalyzers;

    [ObservableProperty]
    private bool _noDocs;

    [ObservableProperty]
    private bool _parallel;

    [ObservableProperty]
    private bool _snabb;
}
