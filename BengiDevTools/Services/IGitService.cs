namespace BengiDevTools.Services;

public interface IGitService
{
    Task<string> GetStatusAsync(string repoPath, CancellationToken ct = default);
}
