using BengiDevTools.Models;

namespace BengiDevTools.Services;

public interface IBuildService
{
    Task BuildAsync(
        IEnumerable<RepoBuildTarget> targets,
        BuildFlags flags,
        IProgress<(string repo, string status)> progress,
        Action<string> onOutputLine,
        CancellationToken ct);
}
