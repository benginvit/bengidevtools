using BengiDevTools.Models;

namespace BengiDevTools.Services;

public interface IProcessService
{
    event EventHandler<(string appName, bool isRunning)>? AppStateChanged;
    bool IsRunning(string appName);
    Task StartAsync(AppDefinition app, string repoRootPath);
    Task StopAsync(AppDefinition app);
    Task RestartAsync(AppDefinition app, string repoRootPath);
}
