namespace BengiDevTools.Models;

public class AppSettings
{
    public string RepoRootPath { get; set; } = @"C:\TFS\Repos";
    public string SqlConnectionString { get; set; } = "Server=localhost;Integrated Security=true;TrustServerCertificate=true;";
    public string DebugScriptsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DebugScripts");

    // Projekt/repon att utesluta från scan (skiftlägesokänslig substring-matchning mot RepoName eller ProjectName)
    public List<string> ExcludedProjects { get; set; } = [];
}
