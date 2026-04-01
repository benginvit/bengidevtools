namespace BengiDevTools.Services;

public interface IProcessService
{
    bool IsRunning(string id);
    Task StartAsync(string id, string csprojPath, string? launchProfile = null);
    Task StopAsync(string id);
    Task RestartAsync(string id, string csprojPath, string? launchProfile = null);
}
