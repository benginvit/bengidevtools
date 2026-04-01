namespace BengiDevTools.Services;

public interface IProcessService
{
    bool IsRunning(string name);
    string GetGitStatus(string name);
    void SetGitStatus(string name, string status);
    Task StartAsync(AppEntry entry, string repoRootPath);
    Task StopAsync(string name);
    Task RestartAsync(AppEntry entry, string repoRootPath);
}
