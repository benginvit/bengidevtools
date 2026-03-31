using CommunityToolkit.Mvvm.ComponentModel;

namespace BengiDevTools.Models;

public partial class BuildFlags : ObservableObject
{
    [ObservableProperty]
    public partial bool NoRestore { get; set; }

    [ObservableProperty]
    public partial bool NoAnalyzers { get; set; }

    [ObservableProperty]
    public partial bool NoDocs { get; set; }

    [ObservableProperty]
    public partial bool Parallel { get; set; }

    [ObservableProperty]
    public partial bool Snabb { get; set; }
}
