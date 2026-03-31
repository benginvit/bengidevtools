using System.Diagnostics;
using BengiDevTools.Models;

namespace BengiDevTools.Services;

public class ProcessService : IProcessService
{
    private readonly Dictionary<string, Process> _processes = new();

    public event EventHandler<(string appName, bool isRunning)>? AppStateChanged;

    public bool IsRunning(string appName) =>
        _processes.TryGetValue(appName, out var p) && !p.HasExited;

    public async Task StartAsync(AppDefinition app, string repoRootPath)
    {
        if (IsRunning(app.Name))
            return;

        var csprojPath = FindCsproj(app, repoRootPath);
        if (csprojPath is null)
            return;

        var workDir = Path.GetDirectoryName(csprojPath)!;

        var psi = new ProcessStartInfo("dotnet")
        {
            Arguments = $"run --no-build --project \"{csprojPath}\"",
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Exited += (_, _) =>
        {
            _processes.Remove(app.Name);
            AppStateChanged?.Invoke(this, (app.Name, false));
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        _processes[app.Name] = proc;
        AppStateChanged?.Invoke(this, (app.Name, true));

        await Task.CompletedTask;
    }

    public async Task StopAsync(AppDefinition app)
    {
        if (!_processes.TryGetValue(app.Name, out var proc))
            return;

        try
        {
            proc.Kill(entireProcessTree: true);
            await proc.WaitForExitAsync();
        }
        catch { }

        _processes.Remove(app.Name);
        AppStateChanged?.Invoke(this, (app.Name, false));
    }

    public async Task RestartAsync(AppDefinition app, string repoRootPath)
    {
        await StopAsync(app);
        await Task.Delay(500);
        await StartAsync(app, repoRootPath);
    }

    private static string? FindCsproj(AppDefinition app, string repoRootPath)
    {
        if (!AppRegistry.RepoMap.TryGetValue(app.RepoKey, out var repoFolder))
            return null;

        var repoPath = Path.Combine(repoRootPath, repoFolder);
        if (!Directory.Exists(repoPath))
            return null;

        return Directory
            .GetFiles(repoPath, $"{app.ProjectName}.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();
    }
}
