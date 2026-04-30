using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace BengiDevTools.Services;

public partial class ProcessService : IProcessService
{
    [GeneratedRegex(@"\w+Exception:|\bUnhandled\b|fail:|crit:", RegexOptions.IgnoreCase)]
    private static partial Regex ExceptionPattern();

    private readonly object                          _lock         = new();
    private readonly Dictionary<string, Process>    _processes    = new();
    private readonly Dictionary<string, AppOutput>  _outputs      = new();
    private readonly Dictionary<string, int>        _externalPids = new(); // id → pid for externally detected

    public bool IsRunning(string id)
    {
        lock (_lock) return _processes.TryGetValue(id, out var p) && !p.HasExited;
    }

    public bool IsExternal(string id) => _externalPids.ContainsKey(id);
    public int GetPid(string id)
    {
        Process? p; lock (_lock) _processes.TryGetValue(id, out p);
        if (p is not null && !p.HasExited)
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

                    // 1. App hosted by dotnet.exe — cmdline contains "ProjectName.dll"
                    var dllPid = _lastDotnetCmdlines
                        .Where(c => c.Contains(projectName + ".dll", StringComparison.OrdinalIgnoreCase))
                        .Select(c => int.TryParse(c.Split(' ', 2)[0], out var parsed) ? parsed : 0)
                        .FirstOrDefault(parsed => parsed > 0);
                    if (dllPid > 0) return dllPid;

                    // 2. App running as Windows apphost (ProjectName.exe), not dotnet.exe
                    try
                    {
                        var procs = Process.GetProcessesByName(projectName);
                        if (procs.Length > 0) return procs[0].Id;
                    }
                    catch { }
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

    private static void KillProcessTreeLinux(int pid)
    {
        // Recursively kill all descendants, then the process itself
        try
        {
            var psi = new ProcessStartInfo("/bin/bash", $"-c \"pgrep -P {pid} 2>/dev/null\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(500);
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(line.Trim(), out var childPid))
                    KillProcessTreeLinux(childPid);
        }
        catch { }
        try { Process.GetProcessById(pid).Kill(); } catch { }
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
        HashSet<string> managedIds; lock (_lock) managedIds = new HashSet<string>(_processes.Keys);

        // Read listening ports and dotnet cmdlines once per poll — no .NET /proc access.
        _lastListeningPorts = ReadListeningPorts();
        _lastDotnetCmdlines = ReadDotnetCmdlines(out _lastCmdlineError);

        foreach (var app in _lastApps)
        {
            if (managedIds.Contains(app.Id)) continue;

            var projectName = Path.GetFileNameWithoutExtension(app.CsprojPath);
            var dllName     = projectName + ".dll";

            // Match on "Foo.dll" — avoids matching the "dotnet run --project Foo.csproj" wrapper.
            var found = app.HttpsPort.HasValue
                ? _lastListeningPorts.Contains(app.HttpsPort.Value)
                : _lastDotnetCmdlines.Any(c => c.Contains(dllName, StringComparison.OrdinalIgnoreCase));

            // Fallback: app might run as ProjectName.exe (Windows apphost)
            if (!found && OperatingSystem.IsWindows())
                try { found = Process.GetProcessesByName(projectName).Length > 0; } catch { }

            if (found)
            {
                var pid = _lastDotnetCmdlines
                    .Where(c => c.Contains(dllName, StringComparison.OrdinalIgnoreCase))
                    .Select(c => int.TryParse(c.Split(' ', 2)[0], out var p) ? p : 0)
                    .FirstOrDefault(p => p > 0);

                if (pid <= 0 && OperatingSystem.IsWindows())
                    try
                    {
                        var procs = Process.GetProcessesByName(projectName);
                        if (procs.Length > 0) pid = procs[0].Id;
                    }
                    catch { }

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

        var projectDir = Path.GetDirectoryName(csprojPath);

        await FreeOccupiedPortsAsync(projectDir, launchProfile);
        await FreeHealthCheckPortAsync(projectDir);

        var args = $"run --no-build --project \"{csprojPath}\"";
        if (launchProfile is not null) args += $" --launch-profile \"{launchProfile}\"";

        var psi = new ProcessStartInfo("dotnet")
        {
            Arguments              = args,
            WorkingDirectory       = projectDir,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };

        psi.Environment["ASPNETCORE_ENVIRONMENT"]          = "Development";
        psi.Environment["HealthChecksWebServer__Enabled"]  = "false";
        // Don't inherit BengiDevTools' own ASPNETCORE_URLS (port 5050) into child processes
        psi.Environment.Remove("ASPNETCORE_URLS");

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Exited += (_, _) => { lock (_lock) _processes.Remove(id); };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            output.Append(e.Data, ExceptionPattern());
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            output.Append(e.Data, ExceptionPattern());
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        lock (_lock) _processes[id] = proc;

        await Task.CompletedTask;
    }

    public async Task StopAsync(string id)
    {
        // Kill externally detected process by scanning /proc again for exact pid
        if (_externalPids.TryGetValue(id, out var externalPid))
        {
            _externalPids.Remove(id);
            if (externalPid > 0)
            {
                if (OperatingSystem.IsLinux())
                    KillProcessTreeLinux(externalPid);
                try { Process.GetProcessById(externalPid).Kill(entireProcessTree: true); } catch { }
            }
            else
            {
                // Fallback: kill by port
                foreach (var app in _lastApps.Where(a => a.Id == id && a.HttpsPort.HasValue))
                    try
                    {
                        var fuser = Process.Start(new ProcessStartInfo("fuser", $"-k {app.HttpsPort!.Value}/tcp")
                            { UseShellExecute = false });
                        fuser?.WaitForExit(2000);
                    }
                    catch { }
            }
            return;
        }

        Process? proc; lock (_lock) _processes.TryGetValue(id, out proc);
        if (proc is null) return;

        // On Linux, dotnet run spawns the app as a child — kill it explicitly first
        if (OperatingSystem.IsLinux())
        {
            try
            {
                KillProcessTreeLinux(proc.Id);
                await Task.Delay(200);
            }
            catch { }
        }

        try
        {
            proc.Kill(entireProcessTree: true);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await proc.WaitForExitAsync(cts.Token);
        }
        catch { }
        lock (_lock) _processes.Remove(id);
    }

    private IEnumerable<ScannedApp> _lastApps = [];

    private static async Task FreeOccupiedPortsAsync(string? projectDir, string? launchProfile)
    {
        if (projectDir is null || launchProfile is null) return;
        var path = Path.Combine(projectDir, "Properties", "launchSettings.json");
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles)) return;
            foreach (var p in profiles.EnumerateObject())
            {
                if (!p.Name.Equals(launchProfile, StringComparison.OrdinalIgnoreCase)) continue;
                if (!p.Value.TryGetProperty("applicationUrl", out var urlProp)) return;
                foreach (var segment in (urlProp.GetString() ?? "").Split(';'))
                    if (Uri.TryCreate(segment.Trim(), UriKind.Absolute, out var uri))
                        await KillPortAsync(uri.Port);
                return;
            }
        }
        catch { }
    }

    private static async Task FreeHealthCheckPortAsync(string? projectDir)
    {
        if (projectDir is null) return;
        foreach (var filename in new[] { "appsettings.Development.json", "appsettings.json" })
        {
            var path = Path.Combine(projectDir, filename);
            if (!File.Exists(path)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("HealthChecksWebServer", out var hc) &&
                    hc.TryGetProperty("Port", out var portProp) &&
                    portProp.TryGetInt32(out var port) && port > 0)
                {
                    await KillPortAsync(port);
                    return;
                }
            }
            catch { }
        }
    }

    private static async Task KillPortAsync(int port)
    {
        var inUse = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Any(ep => ep.Port == port);
        if (!inUse) return;

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var proc = Process.Start(new ProcessStartInfo("/bin/bash", $"-c \"fuser -k {port}/tcp 2>/dev/null\"")
                    { UseShellExecute = false, CreateNoWindow = true });
                if (proc is not null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
            }
            catch { }
        }
        else if (OperatingSystem.IsWindows())
        {
            try
            {
                var psi = new ProcessStartInfo("cmd", $"/c for /f \"tokens=5\" %a in ('netstat -aon ^| findstr :{port} ^| findstr LISTENING') do taskkill /F /PID %a")
                    { UseShellExecute = false, CreateNoWindow = true };
                var proc = Process.Start(psi);
                if (proc is not null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
            }
            catch { }
        }
    }

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

        public void Append(string line, Regex exceptionPattern)
        {
            lock (this)
            {
                if (_lines.Count >= MaxLines) _lines.RemoveAt(0);
                _lines.Add(line);

                if (!HasException && exceptionPattern.IsMatch(line))
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
