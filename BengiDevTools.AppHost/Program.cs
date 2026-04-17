using System.Text.Json;
using Aspire.Hosting;

// Resolve DCP and Dashboard paths from NuGet cache (normally done by Aspire SDK via MSBuild)
SetAspirePaths();

var builder = DistributedApplication.CreateBuilder(args);

// ── Read BengiDevTools settings ──────────────────────────────────────────────

var settingsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "BengiDevTools", "settings.json");

var settings = LoadSettings(settingsPath);
var repoRoot = settings.RepoRootPath;
var excluded = settings.ExcludedProjects;

Console.WriteLine($"Repo-rot: {repoRoot}");

// ── Discover and register projects ───────────────────────────────────────────

if (!Directory.Exists(repoRoot))
{
    Console.WriteLine($"Varning: repo-rot saknas ({repoRoot}). Starta med bara Aspire-dashboarden.");
}
else
{
    var repoDirs = Directory.GetDirectories(repoRoot).OrderBy(d => d);

    foreach (var repoDir in repoDirs)
    {
        var repoName = Path.GetFileName(repoDir);

        if (IsExcluded(repoName, excluded))
        {
            Console.WriteLine($"  Hoppar över (exkluderad): {repoName}");
            continue;
        }

        var csprojFiles = Directory
            .GetFiles(repoDir, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(f => f);

        foreach (var csproj in csprojFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(csproj);

            if (IsTestProject(projectName)) continue;
            if (IsExcluded(projectName, excluded)) continue;
            if (!IsRunnableProject(csproj)) continue;

            var resourceName = ToResourceName($"{repoName}-{projectName}");
            var workDir = Path.GetDirectoryName(csproj)!;
            var profile = GetLaunchProfile(csproj);

            Console.WriteLine($"  + {resourceName}");

            var resource = builder.AddExecutable(
                resourceName,
                "dotnet",
                workDir,
                profile is not null
                    ? ["run", "--project", csproj, "--launch-profile", profile]
                    : ["run", "--project", csproj]);

            // Inject Aspire OTLP endpoint so logs/traces appear in the dashboard
            resource.WithOtlpExporter();
        }
    }
}

builder.Build().Run();

// ── Helpers ───────────────────────────────────────────────────────────────────

static SettingsDto LoadSettings(string path)
{
    if (!File.Exists(path)) return new SettingsDto();
    try { return JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(path)) ?? new SettingsDto(); }
    catch { return new SettingsDto(); }
}

static bool IsExcluded(string name, List<string> excluded) =>
    excluded.Any(e => name.Contains(e, StringComparison.OrdinalIgnoreCase));

static bool IsTestProject(string name) =>
    name.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("Spec", StringComparison.OrdinalIgnoreCase);

static bool IsRunnableProject(string csproj)
{
    try
    {
        var content = File.ReadAllText(csproj);
        return content.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
            || content.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase);
    }
    catch { return false; }
}

static string? GetLaunchProfile(string csproj)
{
    try
    {
        var launchPath = Path.Combine(Path.GetDirectoryName(csproj)!, "Properties", "launchSettings.json");
        if (!File.Exists(launchPath)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(launchPath));
        if (!doc.RootElement.TryGetProperty("profiles", out var profiles)) return null;
        foreach (var p in profiles.EnumerateObject())
        {
            if (p.Name.Equals("https", StringComparison.OrdinalIgnoreCase)) return p.Name;
        }
        foreach (var p in profiles.EnumerateObject())
        {
            if (p.Value.TryGetProperty("applicationUrl", out _)) return p.Name;
        }
        return null;
    }
    catch { return null; }
}

// Aspire resource names: lowercase letters, digits, hyphens only
static string ToResourceName(string name) =>
    new string(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray())
        .Trim('-');

static void SetAspirePaths()
{
    var nuget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    var dcpDir = Directory
        .GetDirectories(Path.Combine(nuget, "aspire.hosting.orchestration.linux-x64"))
        .OrderDescending().FirstOrDefault();
    var dashDir = Directory
        .GetDirectories(Path.Combine(nuget, "aspire.dashboard.sdk.linux-x64"))
        .OrderDescending().FirstOrDefault();

    if (dcpDir is not null)
        Environment.SetEnvironmentVariable("DcpPublisher__CliPath",
            Path.Combine(dcpDir, "tools", "dcp"));
    if (dashDir is not null)
        // Aspire expects path without .dll extension — it appends .dll itself
        Environment.SetEnvironmentVariable("DcpPublisher__DashboardPath",
            Path.Combine(dashDir, "tools", "Aspire.Dashboard"));

    // Dashboard URL and OTLP endpoint (normally injected by Aspire SDK launch profile)
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS",                      "http://0.0.0.0:18888");
    Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL",   "http://0.0.0.0:18889");
    Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL",   "http://0.0.0.0:18889");
    Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT",     "true");
    Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");
}

record SettingsDto(
    string RepoRootPath = @"C:\TFS\Repos",
    string SqlConnectionString = "",
    string DebugScriptsPath = "",
    List<string>? ExcludedProjects = null)
{
    public List<string> ExcludedProjects { get; init; } = ExcludedProjects ?? [];
}
