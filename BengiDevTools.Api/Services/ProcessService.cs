using System.Diagnostics;

namespace BengiDevTools.Services;

public class ProcessService : IProcessService
{
    private readonly Dictionary<string, Process> _processes = new();

    public bool IsRunning(string id) =>
        _processes.TryGetValue(id, out var p) && !p.HasExited;

    public async Task StartAsync(string id, string csprojPath, string? launchProfile = null)
    {
        if (IsRunning(id)) return;

        var args = $"run --no-build --project \"{csprojPath}\"";
        if (launchProfile is not null) args += $" --launch-profile \"{launchProfile}\"";

        var psi = new ProcessStartInfo("dotnet")
        {
            Arguments        = args,
            WorkingDirectory = Path.GetDirectoryName(csprojPath)!,
            UseShellExecute  = false,
            CreateNoWindow   = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Exited += (_, _) => _processes.Remove(id);
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        _processes[id] = proc;

        await Task.CompletedTask;
    }

    public async Task StopAsync(string id)
    {
        if (!_processes.TryGetValue(id, out var proc)) return;
        try { proc.Kill(entireProcessTree: true); await proc.WaitForExitAsync(); } catch { }
        _processes.Remove(id);
    }

    public async Task RestartAsync(string id, string csprojPath, string? launchProfile = null)
    {
        await StopAsync(id);
        await Task.Delay(500);
        await StartAsync(id, csprojPath, launchProfile);
    }
}
