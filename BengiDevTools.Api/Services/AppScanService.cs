using System.Text.Json;

namespace BengiDevTools.Services;

public record ScannedApp(
    string Id,
    string RepoName,
    string ProjectName,
    string CsprojPath,
    int?   HttpsPort,
    string? LaunchProfile);

public class AppScanService
{
    private readonly ISettingsService _settings;
    private List<ScannedApp> _cache = [];
    private readonly Dictionary<string, string> _gitStatuses = new();

    public AppScanService(ISettingsService settings) => _settings = settings;

    public IReadOnlyList<ScannedApp> Cached => _cache;

    public ScannedApp? GetById(string id) =>
        _cache.FirstOrDefault(a => a.Id == id);

    public string GetGitStatus(string repoName) =>
        _gitStatuses.TryGetValue(repoName, out var s) ? s : "–";

    public void SetGitStatus(string repoName, string status) =>
        _gitStatuses[repoName] = status;

    public IReadOnlyList<ScannedApp> Scan()
    {
        var root = _settings.Settings.RepoRootPath;
        if (!Directory.Exists(root))
            return _cache = [];

        _cache = Directory.GetDirectories(root)
            .OrderBy(d => d)
            .SelectMany(ScanRepo)
            .ToList();

        return _cache;
    }

    private static IEnumerable<ScannedApp> ScanRepo(string repoDir)
    {
        var repoName = Path.GetFileName(repoDir);

        return Directory
            .GetFiles(repoDir, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .Select(csproj => TryBuildScannedApp(repoName, csproj))
            .Where(a => a is not null)
            .Cast<ScannedApp>();
    }

    private static ScannedApp? TryBuildScannedApp(string repoName, string csproj)
    {
        var projectDir = Path.GetDirectoryName(csproj)!;
        var launchSettingsPath = Path.Combine(projectDir, "Properties", "launchSettings.json");
        if (!File.Exists(launchSettingsPath)) return null;

        var projectName = Path.GetFileNameWithoutExtension(csproj);
        int?    httpsPort     = null;
        string? launchProfile = null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles)) return null;

            // Föredra "https"-profil, annars första med applicationUrl
            JsonElement? chosenProfile = null;
            string?      chosenName   = null;

            foreach (var p in profiles.EnumerateObject())
            {
                if (!p.Value.TryGetProperty("applicationUrl", out _)) continue;
                if (chosenProfile is null) { chosenProfile = p.Value; chosenName = p.Name; }
                if (p.Name.Equals("https", StringComparison.OrdinalIgnoreCase))
                    { chosenProfile = p.Value; chosenName = p.Name; break; }
            }

            if (chosenProfile is null) return null;
            launchProfile = chosenName;

            if (chosenProfile.Value.TryGetProperty("applicationUrl", out var urlProp))
            {
                foreach (var segment in (urlProp.GetString() ?? "").Split(';'))
                {
                    var url = segment.Trim();
                    if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        httpsPort = uri.Port;
                        break;
                    }
                }
            }
        }
        catch { return null; }

        return new ScannedApp(
            Id:            $"{repoName}/{projectName}",
            RepoName:      repoName,
            ProjectName:   projectName,
            CsprojPath:    csproj,
            HttpsPort:     httpsPort,
            LaunchProfile: launchProfile);
    }
}
