using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace BengiDevTools.Services;

public partial class ProcessService : IProcessService
{
    [GeneratedRegex(@"\bException\b|\bUnhandled\b|fail:|crit:", RegexOptions.IgnoreCase)]
    private static partial Regex ExceptionPattern();

    private readonly Dictionary<string, Process>   _processes = new();
    private readonly Dictionary<string, AppOutput> _outputs   = new();
    private readonly Dictionary<string, int>       _externalPids = new(); // id → pid for externally detected

    public bool IsRunning(string id) =>
        _processes.TryGetValue(id, out var p) && !p.HasExited;

    public bool IsExternal(string id) => _externalPids.ContainsKey(id);
    public int GetPid(string id)
    {
        if (_processes.TryGetValue(id, out var p) && !p.HasExited)
        {
            // On Windows: dotnet run → MSBuild (intermediate) → App.dll (target).
            // Skip the child lookup and instead find the app process by project name in
            // cmdlines, filtering out MSBuild entries.
            if (OperatingSystem.IsWindows())
            {
                var app = _lastApps.FirstOrDefault(a => a.Id == id);
                if (app is not null)
                {
                    var projectName = Path.GetFileNameWithoutExtension(app.CsprojPath);
                    var appPid = _lastDotnetCmdlines
                        .Where(c => c.Contains(projectName, StringComparison.OrdinalIgnoreCase)
                                 && !c.Contains("MSBuild", StringComparison.OrdinalIgnoreCase))
                        .Select(c => int.TryParse(c.Split(' ', 2)[0], out var parsed) ? parsed : 0)
                        .FirstOrDefault(parsed => parsed > 0);
                    if (appPid > 0) return appPid;
                }
            }
            // On Linux: dotnet run spawns the actual app as a child process.
            var child = GetChildPid(p.Id);
            return child > 0 ? child : p.Id;
        }
        return _externalPids.GetValueOrDefault(id, -1);
    }

    private static int GetChildPid(int parentPid)
    {
        return OperatingSystem.IsWindows()
            ? GetChildPidWindows(parentPid)
            : GetChildPidLinux(parentPid);
    }

    private static int GetChildPidLinux(int parentPid)
    {
        try
        {
            var psi = new ProcessStartInfo("/bin/bash", $"-c \"pgrep -P {parentPid} 2>/dev/null | head -1\"")
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return 0;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(500);
            return int.TryParse(output, out var pid) ? pid : 0;
        }
        catch { return 0; }
    }

    private static int GetChildPidWindows(int parentPid)
    {
        // Not used — Windows uses cmdline search instead (see GetPid)
        return 0;
    }

    public bool HasException(string id) =>
        _outputs.TryGetValue(id, out var o) && o.HasException;

    // Detect externally started processes for a list of apps.
    // Called from the scan/status endpoint.
    public Task DetectExternalAsync(IEnumerable<ScannedApp> apps)
    {
        _lastApps = apps.ToList();
        var managedIds = new HashSet<string>(_processes.Keys);

        // Read listening ports and dotnet cmdlines once per poll — no .NET /proc access.
        _lastListeningPorts = ReadListeningPorts();
        _lastDotnetCmdlines = ReadDotnetCmdlines(out _lastCmdlineError);

        foreach (var app in _lastApps)
        {
            if (managedIds.Contains(app.Id)) continue;

            // Match on project name (no extension) — covers both:
            //   dotnet run --project Foo.csproj  (launched by our tool)
            //   dotnet Foo.dll                   (launched by VS Code debugger)
            var projectName = Path.GetFileNameWithoutExtension(app.CsprojPath);
            var found = app.HttpsPort.HasValue
                ? _lastListeningPorts.Contains(app.HttpsPort.Value)
                : _lastDotnetCmdlines.Any(c => c.Contains(projectName, StringComparison.Ordinal));

            if (found)
            {
                // Try to extract actual PID from pgrep cmdlines
                var pid = _lastDotnetCmdlines
                    .Where(c => c.Contains(projectName, StringComparison.Ordinal))
                    .Select(c => int.TryParse(c.Split(' ', 2)[0], out var p) ? p : 0)
                    .FirstOrDefault(p => p > 0);
                _externalPids[app.Id] = pid > 0 ? pid : -1;
            }
            else
                _externalPids.Remove(app.Id);
        }

        return Task.CompletedTask;
    }

    // Exposed for the debug endpoint
    private HashSet<int>  _lastListeningPorts = [];
    private List<string>  _lastDotnetCmdlines = [];
    private string        _lastCmdlineError   = "";

    public object GetDetectionDiagnostics() => new
    {
        os              = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        isLinux         = OperatingSystem.IsLinux(),
        isWindows       = OperatingSystem.IsWindows(),
        bashExists      = File.Exists("/bin/bash"),
        cmdlineError    = _lastCmdlineError,
        listeningPorts  = _lastListeningPorts.OrderBy(p => p).ToList(),
        dotnetCmdlines  = _lastDotnetCmdlines,
        apps = _lastApps.Select(a => new
        {
            a.Id,
            a.HttpsPort,
            projectName  = Path.GetFileNameWithoutExtension(a.CsprojPath),
            portFound    = a.HttpsPort.HasValue && _lastListeningPorts.Contains(a.HttpsPort.Value),
            cmdlineFound = !a.HttpsPort.HasValue && _lastDotnetCmdlines.Any(
                c => c.Contains(Path.GetFileNameWithoutExtension(a.CsprojPath), StringComparison.Ordinal)),
        }),
    };

    // Use IPGlobalProperties — cross-platform, no /proc access, no exceptions.
    private static HashSet<int> ReadListeningPorts()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Select(ep => ep.Port)
                .ToHashSet();
        }
        catch { return []; }
    }

    private static List<string> ReadDotnetCmdlines(out string error)
    {
        error = "";
        try
        {
            string shell, args;
            if (OperatingSystem.IsWindows())
            {
                // PowerShell EncodedCommand avoids all quoting issues.
                // Output format: "PID CommandLine" per line — same as pgrep -af on Linux.
                var script  = "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | ForEach-Object { $_.ProcessId.ToString() + ' ' + $_.CommandLine }";
                var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
                shell = "powershell.exe";
                args  = $"-NoProfile -EncodedCommand {encoded}";
            }
            else
            {
                shell = "/bin/bash";
                args  = "-c \"pgrep -af dotnet 2>/dev/null\"";
            }

            var psi = new ProcessStartInfo(shell, args)
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) { error = "Process.Start returned null"; return []; }
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return [];
        }
    }


    public IReadOnlyList<string> GetOutputBuffer(string id) =>
        _outputs.TryGetValue(id, out var o) ? o.GetLines() : [];

    public void Subscribe(string id, Channel<string> channel)
    {
        if (_outputs.TryGetValue(id, out var o)) o.Subscribe(channel);
    }

    public void Unsubscribe(string id, Channel<string> channel)
    {
        if (_outputs.TryGetValue(id, out var o)) o.Unsubscribe(channel);
    }

    public async Task StartAsync(string id, string csprojPath, string? launchProfile = null)
    {
        if (IsRunning(id)) return;
        _externalPids.Remove(id);

        var output = _outputs.GetValueOrDefault(id) ?? new AppOutput();
        output.Reset();
        _outputs[id] = output;

        var args = $"run --project \"{csprojPath}\"";
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

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            output.Append(e.Data, isError: false, ExceptionPattern());
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            output.Append(e.Data, isError: true, ExceptionPattern());
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        _processes[id] = proc;

        await Task.CompletedTask;
    }

    public async Task StopAsync(string id)
    {
        // Kill externally detected process by scanning /proc again for exact pid
        if (_externalPids.ContainsKey(id))
        {
            _externalPids.Remove(id);
            // Find and kill the external dotnet process
            // We re-scan since we only stored a placeholder pid=1
            foreach (var app in _lastApps.Where(a => a.Id == id))
            {
                // Kill by port if web app
                if (app.HttpsPort.HasValue)
                {
                    try
                    {
                        var fuser = Process.Start(new ProcessStartInfo("fuser", $"-k {app.HttpsPort.Value}/tcp")
                            { UseShellExecute = false });
                        fuser?.WaitForExit(2000);
                    }
                    catch { }
                }
            }
            return;
        }

        if (!_processes.TryGetValue(id, out var proc)) return;
        try { proc.Kill(entireProcessTree: true); await proc.WaitForExitAsync(); } catch { }
        _processes.Remove(id);
    }

    private IEnumerable<ScannedApp> _lastApps = [];

    public async Task RestartAsync(string id, string csprojPath, string? launchProfile = null)
    {
        await StopAsync(id);
        await Task.Delay(500);
        await StartAsync(id, csprojPath, launchProfile);
    }

    // ── Per-process output state ──────────────────────────────────────────────

    private sealed class AppOutput
    {
        private readonly List<string>          _lines = new();
        private readonly List<Channel<string>> _subs  = new();
        private const int MaxLines = 1000;

        public bool HasException { get; private set; }

        public void Reset()
        {
            lock (this) { _lines.Clear(); HasException = false; }
        }

        public void Append(string line, bool isError, Regex exceptionPattern)
        {
            lock (this)
            {
                if (_lines.Count >= MaxLines) _lines.RemoveAt(0);
                _lines.Add(line);

                if (!HasException && (isError || exceptionPattern.IsMatch(line)))
                    HasException = true;

                foreach (var ch in _subs)
                    ch.Writer.TryWrite(line);
            }
        }

        public IReadOnlyList<string> GetLines()
        {
            lock (this) return _lines.ToList();
        }

        public void Subscribe(Channel<string> ch) { lock (this) _subs.Add(ch); }
        public void Unsubscribe(Channel<string> ch) { lock (this) _subs.Remove(ch); }
    }
}
