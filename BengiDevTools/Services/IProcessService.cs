using System.Threading.Channels;

namespace BengiDevTools.Services;

public interface IProcessService
{
    bool IsRunning(string id);
    bool IsExternal(string id);
    bool HasException(string id);
    Task DetectExternalAsync(IEnumerable<ScannedApp> apps);
    int GetPid(string id);
    object GetDetectionDiagnostics();
    IReadOnlyList<string> GetOutputBuffer(string id);
    void Subscribe(string id, Channel<string> channel);
    void Unsubscribe(string id, Channel<string> channel);
    Task StartAsync(string id, string csprojPath, string? launchProfile = null);
    Task StopAsync(string id);
    Task RestartAsync(string id, string csprojPath, string? launchProfile = null);
}
