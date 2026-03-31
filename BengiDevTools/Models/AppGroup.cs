using System.Collections.ObjectModel;

namespace BengiDevTools.Models;

public class AppGroup
{
    public required string Name { get; init; }
    public ObservableCollection<AppDefinition> Apps { get; init; } = new();
}
