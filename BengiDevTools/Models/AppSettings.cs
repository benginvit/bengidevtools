namespace BengiDevTools.Models;

public class AppSettings
{
    public string SqlConnectionString { get; set; } = "Server=localhost;Integrated Security=true;TrustServerCertificate=true;";
    public string DebugScriptsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DebugScripts");
}
