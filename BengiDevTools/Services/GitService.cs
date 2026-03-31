using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BengiDevTools.Services;

public partial class GitService : IGitService
{
    [GeneratedRegex(@"\[behind \d+\]")]
    private static partial Regex BehindRegex();

    public async Task<string> GetStatusAsync(string repoPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(repoPath))
            return "Saknas";

        try
        {
            await RunGitAsync(repoPath, "fetch --quiet", ct);
            var output = await RunGitAsync(repoPath, "status -b --porcelain=v1", ct);
            var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

            return BehindRegex().IsMatch(firstLine) ? "Bakom" : "Uppdaterad";
        }
        catch (OperationCanceledException)
        {
            return "–";
        }
        catch
        {
            return "Okänd";
        }
    }

    private static async Task<string> RunGitAsync(string repoPath, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git")
        {
            Arguments = arguments,
            WorkingDirectory = repoPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("git kunde inte startas");
        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return output;
    }
}
