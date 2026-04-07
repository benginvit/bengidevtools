using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace BengiDevTools.Services;

public partial class ProcessService : IProcessService
{
    [GeneratedRegex(@"\bException\b|\bUnhandled\b|fail:|crit:", RegexOptions.IgnoreCase)]
    private static partial Regex ExceptionPattern();

    private readonly Dictionary<string, Process>       _processes = new();
    private readonly Dictionary<string, AppOutput>     _outputs   = new();

    public bool IsRunning(string id) =>
        _processes.TryGetValue(id, out var p) && !p.HasExited;

    public bool HasException(string id) =>
        _outputs.TryGetValue(id, out var o) && o.HasException;

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
