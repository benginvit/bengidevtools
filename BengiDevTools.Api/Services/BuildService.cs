using System.Diagnostics;
using BengiDevTools.Models;

namespace BengiDevTools.Services;

public class BuildService : IBuildService
{
    public async Task BuildAsync(
        IEnumerable<RepoBuildTarget> targets,
        BuildFlags flags,
        Action<string, string> onProgress,
        Action<string> onOutputLine,
        CancellationToken ct = default)
    {
        var list = targets.ToList();
        var extraArgs = BuildArgs(flags);

        if (flags.Parallel)
        {
            var sem = new SemaphoreSlim(4);
            var tasks = list.Select(async t =>
            {
                await sem.WaitAsync(ct);
                try   { await BuildOneAsync(t, extraArgs, onProgress, onOutputLine, ct); }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);
        }
        else
        {
            foreach (var t in list)
            {
                ct.ThrowIfCancellationRequested();
                await BuildOneAsync(t, extraArgs, onProgress, onOutputLine, ct);
            }
        }
    }

    private static async Task BuildOneAsync(
        RepoBuildTarget target,
        string extraArgs,
        Action<string, string> onProgress,
        Action<string> onOutputLine,
        CancellationToken ct)
    {
        onProgress(target.RepoName, "Bygger...");
        onOutputLine($"\n▶ {target.RepoName}");

        var psi = new ProcessStartInfo("dotnet")
        {
            Arguments = $"build \"{target.SlnPath}\" --configuration Debug -maxcpucount {extraArgs}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tcs = new TaskCompletionSource<int>();
        proc.Exited += (_, _) => tcs.TrySetResult(proc.ExitCode);
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) onOutputLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) onOutputLine(e.Data); };

        await using var reg = ct.Register(() =>
        {
            try { proc.Kill(true); } catch { }
            tcs.TrySetCanceled(ct);
        });

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var exitCode = await tcs.Task;
        var status = exitCode == 0 ? "OK" : "FAILED";
        onProgress(target.RepoName, status);
        onOutputLine($"{(exitCode == 0 ? "✅" : "❌")} {target.RepoName}: {status}");
    }

    private static string BuildArgs(BuildFlags f)
    {
        var parts = new List<string>();
        if (f.NoRestore)   parts.Add("--no-restore");
        if (f.NoAnalyzers) parts.Add("-p:RunAnalyzers=false");
        if (f.NoDocs)      parts.Add("-p:GenerateDocumentationFile=false");
        return string.Join(" ", parts);
    }
}
