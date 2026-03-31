using System.Diagnostics;
using BengiDevTools.Models;

namespace BengiDevTools.Services;

public class BuildService : IBuildService
{
    public async Task BuildAsync(
        IEnumerable<RepoBuildTarget> targets,
        BuildFlags flags,
        IProgress<(string repo, string status)> progress,
        Action<string> onOutputLine,
        CancellationToken ct)
    {
        var targetList = targets.ToList();
        var buildArgs = BuildArgs(flags);

        if (flags.Parallel)
        {
            var sem = new SemaphoreSlim(4);
            var tasks = targetList.Select(async target =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    await BuildOneAsync(target, buildArgs, progress, onOutputLine, ct);
                }
                finally
                {
                    sem.Release();
                }
            });
            await Task.WhenAll(tasks);
        }
        else
        {
            foreach (var target in targetList)
            {
                ct.ThrowIfCancellationRequested();
                await BuildOneAsync(target, buildArgs, progress, onOutputLine, ct);
            }
        }
    }

    private static async Task BuildOneAsync(
        RepoBuildTarget target,
        string extraArgs,
        IProgress<(string repo, string status)> progress,
        Action<string> onOutputLine,
        CancellationToken ct)
    {
        progress.Report((target.RepoName, "Bygger..."));
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
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) onOutputLine(e.Data); };

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
        progress.Report((target.RepoName, status));
        onOutputLine($"{(exitCode == 0 ? "✅" : "❌")} {target.RepoName}: {status}");
    }

    private static string BuildArgs(BuildFlags flags)
    {
        var parts = new List<string>();
        if (flags.NoRestore)   parts.Add("--no-restore");
        if (flags.NoAnalyzers) parts.Add("-p:RunAnalyzers=false");
        if (flags.NoDocs)      parts.Add("-p:GenerateDocumentationFile=false");
        return string.Join(" ", parts);
    }
}
