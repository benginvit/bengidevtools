using System.Text.Json;

namespace BengiDevTools.Services;

public record ScannedApp(
    string Id,
    string RepoName,
    string ProjectName,
    string CsprojPath,
    int?   HttpsPort,
    string? LaunchProfile)
{
    public string LocalUserPath =>
        Path.Combine(Path.GetDirectoryName(CsprojPath)!, "appsettings.localuser.json");

    public bool HasLocalUser => File.Exists(LocalUserPath);
};

public class AppScanService
{
    private static readonly string CacheFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BengiDevTools", "scan-cache.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ISettingsService _settings;
    private List<ScannedApp> _cache = [];
    private readonly Dictionary<string, string> _gitStatuses = new();
    private readonly Dictionary<string, string> _gitBranches = new();
    public DateTime? LastScanned { get; private set; }

    public AppScanService(ISettingsService settings) => _settings = settings;

    public IReadOnlyList<ScannedApp> Cached => _cache;

    public ScannedApp? GetById(string id) =>
        _cache.FirstOrDefault(a => a.Id == id);

    public string GetGitStatus(string repoName) =>
        _gitStatuses.TryGetValue(repoName, out var s) ? s : "–";

    public string GetGitBranch(string repoName) =>
        _gitBranches.TryGetValue(repoName, out var b) ? b : "";

    public void SetGitStatus(string repoName, string status, string branch = "")
    {
        _gitStatuses[repoName] = status;
        if (!string.IsNullOrEmpty(branch))
            _gitBranches[repoName] = branch;
    }

    // Load persisted cache from disk — call once on startup
    public void LoadCache()
    {
        if (!File.Exists(CacheFilePath)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<ScanCacheFile>(
                File.ReadAllText(CacheFilePath), JsonOpts);
            if (saved is null) return;
            _cache       = saved.Apps ?? [];
            LastScanned  = saved.ScannedAt;
        }
        catch { /* corrupt cache — ignore, user can rescan */ }
    }

    // Full disk scan + persist result
    public IReadOnlyList<ScannedApp> Scan(Action<string>? progress = null)
    {
        var root = _settings.Settings.RepoRootPath;
        if (!Directory.Exists(root))
        {
            progress?.Invoke($"Fel: repo-rot saknas ({root})");
            return _cache = [];
        }

        var excluded = _settings.Settings.ExcludedProjects
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .ToList();

        var repoDirs = Directory.GetDirectories(root).OrderBy(d => d).ToList();
        progress?.Invoke($"Hittade {repoDirs.Count} repon i {root}");

        var result = new List<ScannedApp>();
        foreach (var dir in repoDirs)
        {
            var repoName = Path.GetFileName(dir);
            if (IsExcluded(repoName, excluded))
            {
                progress?.Invoke($"  {repoName}  (exkluderad)");
                continue;
            }
            var apps = ScanRepo(dir, excluded).ToList();
            progress?.Invoke(apps.Count > 0
                ? $"  {repoName}  →  {apps.Count} projekt"
                : $"  {repoName}  (inga körbara projekt)");
            result.AddRange(apps);
        }

        _cache      = result;
        LastScanned = DateTime.UtcNow;
        progress?.Invoke($"Sparar cache ({result.Count} projekt)...");
        SaveCache();
        progress?.Invoke($"Scan klar — {result.Count} projekt totalt");
        return _cache;
    }

    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath)!);
            File.WriteAllText(CacheFilePath,
                JsonSerializer.Serialize(new ScanCacheFile(_cache, LastScanned), JsonOpts));
        }
        catch { /* non-critical */ }
    }

    private record ScanCacheFile(List<ScannedApp> Apps, DateTime? ScannedAt);

    private static bool IsExcluded(string name, List<string> excluded) =>
        excluded.Any(e => name.Contains(e, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<ScannedApp> ScanRepo(string repoDir, List<string>? excluded = null)
    {
        var repoName = Path.GetFileName(repoDir);

        return Directory
            .GetFiles(repoDir, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .Select(csproj => TryBuildScannedApp(repoName, csproj))
            .Where(a => a is not null)
            .Cast<ScannedApp>()
            .Where(a => excluded is null || !IsExcluded(a.ProjectName, excluded));
    }

    private static ScannedApp? TryBuildScannedApp(string repoName, string csproj)
    {
        var projectDir  = Path.GetDirectoryName(csproj)!;
        var projectName = Path.GetFileNameWithoutExtension(csproj);

        // Exkludera testprojekt
        if (projectName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains("Spec", StringComparison.OrdinalIgnoreCase))
            return null;

        var launchSettingsPath = Path.Combine(projectDir, "Properties", "launchSettings.json");
        bool hasLaunchSettings = File.Exists(launchSettingsPath);

        if (!hasLaunchSettings && !IsRunnableProject(csproj))
            return null;

        int?    httpsPort     = null;
        string? launchProfile = null;

        if (hasLaunchSettings)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
                if (doc.RootElement.TryGetProperty("profiles", out var profiles))
                {
                    JsonElement? chosen = null;
                    string?      chosenName = null;

                    foreach (var p in profiles.EnumerateObject())
                    {
                        if (!p.Value.TryGetProperty("applicationUrl", out _)) continue;
                        if (chosen is null) { chosen = p.Value; chosenName = p.Name; }
                        if (p.Name.Equals("https", StringComparison.OrdinalIgnoreCase))
                            { chosen = p.Value; chosenName = p.Name; break; }
                    }

                    launchProfile = chosenName;

                    if (chosen is not null && chosen.Value.TryGetProperty("applicationUrl", out var urlProp))
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
            }
            catch { /* ignorera ogiltiga launchSettings */ }
        }

        return new ScannedApp(
            Id:            $"{repoName}/{projectName}",
            RepoName:      repoName,
            ProjectName:   projectName,
            CsprojPath:    csproj,
            HttpsPort:     httpsPort,
            LaunchProfile: launchProfile);
    }

    private static bool IsRunnableProject(string csproj)
    {
        try
        {
            var content = File.ReadAllText(csproj);
            return content.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
                || content.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
