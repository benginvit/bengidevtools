namespace BengiDevTools.Services;

public interface IGitService
{
    Task<(string Status, string Branch)> GetStatusAsync(string repoPath, CancellationToken ct = default);
    Task<(string Branch, string Message)> CheckoutDefaultAndPullAsync(string repoPath, CancellationToken ct = default);
}
