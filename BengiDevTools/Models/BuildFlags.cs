namespace BengiDevTools.Models;

public class BuildFlags
{
    public bool NoRestore   { get; set; }
    public bool NoAnalyzers { get; set; }
    public bool NoDocs      { get; set; }
    public bool Parallel    { get; set; }
}
