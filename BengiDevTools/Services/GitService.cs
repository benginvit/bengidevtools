using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BengiDevTools.Services;

public partial class GitService : IGitService
{
    [GeneratedRegex(@"\[behind \d+\]")]
    private static partial Regex BehindRegex();

    public async Task<(string Status, string Branch)> GetStatusAsync(string repoPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(repoPath)) return ("Saknas", "");
        try
        {
            await RunGitAsync(repoPath, "fetch --quiet", ct);
            var output = await RunGitAsync(repoPath, "status -b --porcelain=v1", ct);
            var first  = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            return (BehindRegex().IsMatch(first) ? "Bakom" : "Uppdaterad", ParseBranch(first));
        }
        catch (OperationCanceledException) { return ("–", ""); }
        catch                              { return ("Okänd", ""); }
    }

    // Parses "## branchname...origin/branchname [behind N]" → "branchname"
    internal static string ParseBranch(string firstLine)
    {
        if (!firstLine.StartsWith("## ")) return "";
        var rest   = firstLine[3..];
        var dotDot = rest.IndexOf("...", StringComparison.Ordinal);
        return (dotDot > 0 ? rest[..dotDot] : rest.Split(' ')[0]).Trim();
    }

    public async Task<(string Branch, string Message)> CheckoutDefaultAndPullAsync(string repoPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(repoPath)) return ("", "Saknas");
        try
        {
            await RunGitAsync(repoPath, "fetch --quiet", ct);

            foreach (var branch in new[] { "develop", "master", "main" })
            {
                var (_, _, exitCode) = await RunGitRawAsync(repoPath, $"checkout {branch}", ct);
                if (exitCode != 0) continue;

                await RunGitAsync(repoPath, "pull --quiet", ct);
                return (branch, "OK");
            }
            return ("", "Ingen branch (develop/master/main)");
        }
        catch (OperationCanceledException) { return ("", "–"); }
        catch (Exception ex)               { return ("", $"Fel: {ex.Message}"); }
    }

    private static async Task<string> RunGitAsync(string repoPath, string args, CancellationToken ct)
    {
        var (output, _, _) = await RunGitRawAsync(repoPath, args, ct);
        return output;
    }

    private static async Task<(string Output, string Error, int ExitCode)> RunGitRawAsync(string repoPath, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git")
        {
            Arguments        = args,
            WorkingDirectory = repoPath,
            UseShellExecute  = false,
            CreateNoWindow   = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException();
        var outputTask = proc.StandardOutput.ReadToEndAsync(ct);
        var errorTask  = proc.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(outputTask, errorTask);
        await proc.WaitForExitAsync(ct);
        return (outputTask.Result, errorTask.Result, proc.ExitCode);
    }
}
