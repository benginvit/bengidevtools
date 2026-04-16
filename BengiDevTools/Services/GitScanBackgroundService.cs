namespace BengiDevTools.Services;

/// Continuously updates git status for all scanned repos in the background.
/// - git fetch (network) every 5 minutes
/// - git status (local) every 60 seconds
public class GitScanBackgroundService(AppScanService scan, IGitService git) : BackgroundService
{
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FetchInterval  = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Give the app a moment to finish startup
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        var lastFetch = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            var repos = scan.Cached
                .Select(a => a.RepoName)
                .Distinct()
                .ToList();

            if (repos.Count > 0)
            {
                var doFetch = DateTime.UtcNow - lastFetch >= FetchInterval;

                foreach (var repoName in repos)
                {
                    if (ct.IsCancellationRequested) break;
                    var repoPath = GetRepoPath(repoName);
                    if (repoPath is null) continue;
                    var (status, branch) = doFetch
                        ? await git.GetStatusAsync(repoPath, ct)
                        : await GetLocalStatusAsync(repoPath, ct);
                    scan.SetGitStatus(repoName, status, branch);
                }

                if (doFetch) lastFetch = DateTime.UtcNow;
            }

            await Task.Delay(StatusInterval, ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    private string? GetRepoPath(string repoName)
    {
        var app = scan.Cached.FirstOrDefault(a => a.RepoName == repoName);
        if (app is null) return null;
        // CsprojPath is inside the repo dir — go up until we find the repo root
        var dir = Path.GetDirectoryName(app.CsprojPath);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static async Task<(string Status, string Branch)> GetLocalStatusAsync(string repoPath, CancellationToken ct)
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git")
            {
                Arguments              = "status -b --porcelain=v1",
                WorkingDirectory       = repoPath,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            }) ?? throw new InvalidOperationException();
            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var first  = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var status = System.Text.RegularExpressions.Regex.IsMatch(first, @"\[behind \d+\]") ? "Bakom" : "Uppdaterad";
            return (status, GitService.ParseBranch(first));
        }
        catch (OperationCanceledException) { return ("–", ""); }
        catch                              { return ("Okänd", ""); }
    }
}
