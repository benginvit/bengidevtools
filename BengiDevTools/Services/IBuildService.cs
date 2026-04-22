using BengiDevTools.Models;

namespace BengiDevTools.Services;

public interface IBuildService
{
    Task BuildAsync(
        IEnumerable<RepoBuildTarget> targets,
        BuildFlags flags,
        Action<string, string> onProgress,
        Action<string, string> onOutputLine,   // (repoName, line)
        CancellationToken ct = default);
}
