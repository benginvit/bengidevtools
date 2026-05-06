using System.Text.Json;
using System.Threading.Channels;
using BengiDevTools.Models;
using BengiDevTools.Services;

namespace BengiDevTools.Endpoints;

public record BuildStartRequest(
    string[] RepoNames,
    bool NoRestore,
    bool NoAnalyzers,
    bool NoDocs,
    bool Parallel,
    bool Snabb);

public static class BuildEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void MapBuildEndpoints(this WebApplication app)
    {
        // ─── Repos: lista repon med solution-filer ────────────────────────────────

        app.MapGet("/api/repos", (ISettingsService s) =>
        {
            var root = s.Settings.RepoRootPath;
            if (!Directory.Exists(root)) return Results.Ok(Array.Empty<object>());

            var repos = Directory.GetDirectories(root)
                .OrderBy(d => d)
                .Select(dir => new { repoName = Path.GetFileName(dir), slnPath = FindSln(dir) })
                .Where(r => r.slnPath is not null)
                .ToList();

            return Results.Ok(repos);
        });

        // ─── Repos: lista projekt i ett repo ─────────────────────────────────────

        app.MapGet("/api/repos/{repoName}/projects", (string repoName, ISettingsService s) =>
        {
            var dir = Path.Combine(s.Settings.RepoRootPath, repoName);
            if (!Directory.Exists(dir)) return Results.NotFound();
            var projects = Directory.GetFiles(dir, "*.csproj", SearchOption.AllDirectories)
                .OrderBy(p => p)
                .Select(p => new { name = Path.GetFileNameWithoutExtension(p), path = p })
                .ToList();
            return Results.Ok(projects);
        });

        // ─── Build: starta (SSE) ──────────────────────────────────────────────────

        app.MapPost("/api/build/start",
            async (HttpContext ctx, BuildStartRequest req, IBuildService build, ISettingsService s) =>
        {
            ctx.Response.Headers.ContentType  = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection   = "keep-alive";
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

            var root = s.Settings.RepoRootPath;

            var targets = req.RepoNames
                .Select(name =>
                {
                    var dir = Path.Combine(root, name);
                    if (!Directory.Exists(dir)) return null;
                    var sln = FindSln(dir);
                    return sln is null ? null : new RepoBuildTarget { RepoName = name, SlnPath = sln };
                })
                .Where(t => t is not null)
                .Cast<RepoBuildTarget>()
                .ToList();

            var flags = new BuildFlags
            {
                NoRestore   = req.NoRestore   || req.Snabb,
                NoAnalyzers = req.NoAnalyzers || req.Snabb,
                NoDocs      = req.NoDocs      || req.Snabb,
                Parallel    = req.Parallel    || req.Snabb,
            };

            var channel = Channel.CreateUnbounded<string>();

            var buildTask = Task.Run(async () =>
            {
                try
                {
                    await build.BuildAsync(
                        targets, flags,
                        onProgress:   (repo, status) => channel.Writer.TryWrite(
                            JsonSerializer.Serialize(new { type = "progress", repo, status }, JsonOpts)),
                        onOutputLine: (repo, line)   => channel.Writer.TryWrite(
                            JsonSerializer.Serialize(new { type = "output", repo, line }, JsonOpts)),
                        ctx.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    channel.Writer.TryWrite(JsonSerializer.Serialize(
                        new { type = "output", repo = "", line = "⛔ Bygge avbrutet." }, JsonOpts));
                }
                finally { channel.Writer.Complete(); }
            });

            await foreach (var msg in channel.Reader.ReadAllAsync(ctx.RequestAborted))
            {
                await ctx.Response.WriteAsync($"data: {msg}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }

            await buildTask;
            await ctx.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(new { type = "done" }, JsonOpts)}\n\n",
                ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        });
    }

    private static string? FindSln(string dir)
    {
        foreach (var pattern in new[] { "*.sln", "*.slnx" })
        {
            var f = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly).FirstOrDefault()
                 ?? Directory.GetFiles(dir, pattern, SearchOption.AllDirectories).FirstOrDefault();
            if (f is not null) return f;
        }
        return null;
    }
}
