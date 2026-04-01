using System.Diagnostics;

namespace BengiDevTools.Services;

public class ProcessService : IProcessService
{
    private readonly Dictionary<string, Process> _processes = new();
    private readonly Dictionary<string, string>  _gitStatuses =
        AppRegistry.Apps.ToDictionary(a => a.Name, _ => "–");

    public bool IsRunning(string name) =>
        _processes.TryGetValue(name, out var p) && !p.HasExited;

    public string GetGitStatus(string name) =>
        _gitStatuses.TryGetValue(name, out var s) ? s : "–";

    public void SetGitStatus(string name, string status) =>
        _gitStatuses[name] = status;

    public async Task StartAsync(AppEntry entry, string repoRootPath)
    {
        if (IsRunning(entry.Name)) return;

        var csproj = FindCsproj(entry, repoRootPath);
        if (csproj is null) return;

        var psi = new ProcessStartInfo("dotnet")
        {
            Arguments        = $"run --no-build --project \"{csproj}\"",
            WorkingDirectory = Path.GetDirectoryName(csproj)!,
            UseShellExecute  = false,
            CreateNoWindow   = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Exited += (_, _) => _processes.Remove(entry.Name);

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        _processes[entry.Name] = proc;

        await Task.CompletedTask;
    }

    public async Task StopAsync(string name)
    {
        if (!_processes.TryGetValue(name, out var proc)) return;
        try
        {
            proc.Kill(entireProcessTree: true);
            await proc.WaitForExitAsync();
        }
        catch { }
        _processes.Remove(name);
    }

    public async Task RestartAsync(AppEntry entry, string repoRootPath)
    {
        await StopAsync(entry.Name);
        await Task.Delay(500);
        await StartAsync(entry, repoRootPath);
    }

    private static string? FindCsproj(AppEntry entry, string repoRootPath)
    {
        if (!AppRegistry.RepoMap.TryGetValue(entry.RepoKey, out var folder)) return null;
        var repoPath = Path.Combine(repoRootPath, folder);
        if (!Directory.Exists(repoPath)) return null;
        return Directory
            .GetFiles(repoPath, $"{entry.ProjectName}.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();
    }
}
