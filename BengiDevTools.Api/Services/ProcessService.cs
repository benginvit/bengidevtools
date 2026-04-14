using System.Diagnostics;
using System.Net.Sockets;
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

    public bool HasException(string id) =>
        _outputs.TryGetValue(id, out var o) && o.HasException;

    // Detect externally started processes for a list of apps.
    // Called from the scan/status endpoint.
    public async Task DetectExternalAsync(IEnumerable<ScannedApp> apps)
    {
        _lastApps = apps.ToList();
        var managedIds = new HashSet<string>(_processes.Keys);

        await Parallel.ForEachAsync(_lastApps, async (app, _) =>
        {
            if (managedIds.Contains(app.Id)) return; // already ours

            bool found = false;

            // Web app: async TCP connect to HTTPS port with short timeout.
            // Use WhenAny+Delay instead of CancellationToken to avoid OperationCanceledException spam.
            if (app.HttpsPort.HasValue)
            {
                try
                {
                    using var tcp     = new TcpClient();
                    var connectTask   = tcp.ConnectAsync("localhost", app.HttpsPort.Value);
                    var completed     = await Task.WhenAny(connectTask, Task.Delay(300));
                    found = completed == connectTask && !connectTask.IsFaulted && !connectTask.IsCanceled;
                }
                catch { }
            }

            // Console app (no port): scan /proc for matching dotnet cmdline
            if (!found)
                found = FindDotnetProcForCsproj(app.CsprojPath) > 0;

            lock (_externalPids)
            {
                if (found && !_externalPids.ContainsKey(app.Id))
                    _externalPids[app.Id] = 1;
                else if (!found)
                    _externalPids.Remove(app.Id);
            }
        });
    }

    private static int FindDotnetProcForCsproj(string csprojPath)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories("/proc"))
            {
                // Process can die between listing and reading — wrap each read individually
                // to avoid DirectoryNotFoundException spam when using a debugger.
                try
                {
                    var cmdline = File.ReadAllText(Path.Combine(dir, "cmdline")).Replace('\0', ' ');
                    if (cmdline.Contains("dotnet") && cmdline.Contains(csprojPath))
                        return int.TryParse(Path.GetFileName(dir), out var pid) ? pid : 0;
                }
                catch { }
            }
        }
        catch { }
        return 0;
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
                var pid = FindDotnetProcForCsproj(app.CsprojPath);
                if (pid > 0)
                {
                    try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
                }
                // Also kill by port if web app
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
