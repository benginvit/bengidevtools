using System.Diagnostics;
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
    public Task DetectExternalAsync(IEnumerable<ScannedApp> apps)
    {
        _lastApps = apps.ToList();
        var managedIds = new HashSet<string>(_processes.Keys);

        // Read listening ports from /proc/net/tcp(6) once — no sockets, no exceptions.
        var listeningPorts = ReadListeningPorts();

        foreach (var app in _lastApps)
        {
            if (managedIds.Contains(app.Id)) continue;

            var found = app.HttpsPort.HasValue && listeningPorts.Contains(app.HttpsPort.Value);

            if (found && !_externalPids.ContainsKey(app.Id))
                _externalPids[app.Id] = 1;
            else if (!found)
                _externalPids.Remove(app.Id);
        }

        return Task.CompletedTask;
    }

    private static HashSet<int> ReadListeningPorts()
    {
        var ports = new HashSet<int>();
        foreach (var path in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
        {
            try
            {
                foreach (var line in File.ReadLines(path).Skip(1)) // skip header
                {
                    // Format: "sl  local_address rem_address st ..."
                    // local_address = "XXXXXXXX:PPPP", st = "0A" for LISTEN
                    var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;
                    if (parts[3] != "0A") continue; // not LISTEN
                    var colon = parts[1].IndexOf(':');
                    if (colon < 0) continue;
                    if (int.TryParse(parts[1][(colon + 1)..], System.Globalization.NumberStyles.HexNumber, null, out var port))
                        ports.Add(port);
                }
            }
            catch { }
        }
        return ports;
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
