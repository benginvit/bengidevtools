using System.Text.Json;
using System.Threading.Channels;
using BengiDevTools.Models;
using BengiDevTools.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddSingleton<IBuildService,    BuildService>();
builder.Services.AddSingleton<IProcessService,  ProcessService>();
builder.Services.AddSingleton<IGitService,      GitService>();
builder.Services.AddSingleton<AppScanService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();

app.Services.GetRequiredService<ISettingsService>().Load();

var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

// ─── Settings ─────────────────────────────────────────────────────────────────

app.MapGet("/api/settings", (ISettingsService s) => s.Settings);

app.MapPut("/api/settings", (AppSettings body, ISettingsService s) =>
{
    s.Settings.RepoRootPath = body.RepoRootPath;
    s.Save();
    return Results.Ok(s.Settings);
});

// ─── Repos (Build page) ───────────────────────────────────────────────────────

app.MapGet("/api/repos", (ISettingsService s) =>
{
    var root = s.Settings.RepoRootPath;
    if (!Directory.Exists(root)) return Results.Ok(Array.Empty<object>());

    static string? FindSln(string dir)
    {
        foreach (var pattern in new[] { "*.sln", "*.slnx" })
        {
            var f = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly).FirstOrDefault()
                 ?? Directory.GetFiles(dir, pattern, SearchOption.AllDirectories).FirstOrDefault();
            if (f is not null) return f;
        }
        return null;
    }

    var repos = Directory.GetDirectories(root)
        .OrderBy(d => d)
        .Select(dir => new { repoName = Path.GetFileName(dir), slnPath = FindSln(dir) })
        .Where(r => r.slnPath is not null)
        .ToList();

    return Results.Ok(repos);
});

// ─── Apps: scan ───────────────────────────────────────────────────────────────

app.MapGet("/api/apps/scan", (AppScanService scan, IProcessService proc) =>
{
    var apps = scan.Scan();
    return apps.Select(a => new
    {
        a.Id, a.RepoName, a.ProjectName, a.HttpsPort, a.LaunchProfile,
        IsRunning    = proc.IsRunning(a.Id),
        GitStatus    = scan.GetGitStatus(a.RepoName),
        LocalhostUrl = a.HttpsPort.HasValue ? $"https://localhost:{a.HttpsPort}" : null,
    });
});

// Poll running status (ingen re-scan)
app.MapGet("/api/apps/status", (AppScanService scan, IProcessService proc) =>
    scan.Cached.Select(a => new { a.Id, IsRunning = proc.IsRunning(a.Id) }));

// ─── Apps: start / stop / restart ─────────────────────────────────────────────

app.MapPost("/api/apps/start", async (AppActionRequest req, AppScanService scan, IProcessService proc) =>
{
    var a = scan.GetById(req.Id);
    if (a is null) return Results.NotFound();
    await proc.StartAsync(a.Id, a.CsprojPath, a.LaunchProfile);
    return Results.Ok();
});

app.MapPost("/api/apps/stop", async (AppActionRequest req, IProcessService proc) =>
{
    await proc.StopAsync(req.Id);
    return Results.Ok();
});

app.MapPost("/api/apps/restart", async (AppActionRequest req, AppScanService scan, IProcessService proc) =>
{
    var a = scan.GetById(req.Id);
    if (a is null) return Results.NotFound();
    await proc.RestartAsync(a.Id, a.CsprojPath, a.LaunchProfile);
    return Results.Ok();
});

app.MapPost("/api/apps/start-selected", async (string[] ids, AppScanService scan, IProcessService proc) =>
{
    foreach (var id in ids)
    {
        var a = scan.GetById(id);
        if (a is not null) await proc.StartAsync(a.Id, a.CsprojPath, a.LaunchProfile);
    }
    return Results.Ok();
});

app.MapPost("/api/apps/stop-all", async (AppScanService scan, IProcessService proc) =>
{
    foreach (var a in scan.Cached.Where(x => proc.IsRunning(x.Id)))
        await proc.StopAsync(a.Id);
    return Results.Ok();
});

// ─── Apps: git status SSE ─────────────────────────────────────────────────────

app.MapGet("/api/apps/git-refresh", async (HttpContext ctx, AppScanService scan, IGitService git, ISettingsService s) =>
{
    ctx.Response.Headers.ContentType  = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection   = "keep-alive";
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

    var root       = s.Settings.RepoRootPath;
    var channel    = Channel.CreateUnbounded<string>();
    var sem        = new SemaphoreSlim(4);
    var uniqueRepos = scan.Cached.Select(a => a.RepoName).Distinct().ToList();

    var fetchTasks = uniqueRepos.Select(async repoName =>
    {
        await sem.WaitAsync(ctx.RequestAborted);
        try
        {
            var status = await git.GetStatusAsync(Path.Combine(root, repoName), ctx.RequestAborted);
            scan.SetGitStatus(repoName, status);
            channel.Writer.TryWrite(JsonSerializer.Serialize(new { repoName, status }, jsonOpts));
        }
        finally { sem.Release(); }
    });

    _ = Task.WhenAll(fetchTasks).ContinueWith(_ => channel.Writer.Complete());

    await foreach (var msg in channel.Reader.ReadAllAsync(ctx.RequestAborted))
    {
        await ctx.Response.WriteAsync($"data: {msg}\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    }

    await ctx.Response.WriteAsync("event: done\ndata: {}\n\n", ctx.RequestAborted);
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
});

// ─── Build ────────────────────────────────────────────────────────────────────

app.MapPost("/api/build/start", async (HttpContext ctx, BuildStartRequest req, IBuildService build, ISettingsService s) =>
{
    ctx.Response.Headers.ContentType  = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection   = "keep-alive";
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

    var root = s.Settings.RepoRootPath;

    static string? FindSln(string dir)
    {
        foreach (var pattern in new[] { "*.sln", "*.slnx" })
        {
            var f = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly).FirstOrDefault()
                 ?? Directory.GetFiles(dir, pattern, SearchOption.AllDirectories).FirstOrDefault();
            if (f is not null) return f;
        }
        return null;
    }

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
                    JsonSerializer.Serialize(new { type = "progress", repo, status }, jsonOpts)),
                onOutputLine: (repo, line)   => channel.Writer.TryWrite(
                    JsonSerializer.Serialize(new { type = "output", repo, line }, jsonOpts)),
                ctx.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            channel.Writer.TryWrite(
                JsonSerializer.Serialize(new { type = "output", repo = "", line = "⛔ Bygge avbrutet." }, jsonOpts));
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
        $"data: {JsonSerializer.Serialize(new { type = "done" }, jsonOpts)}\n\n", ctx.RequestAborted);
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
});

app.Run("http://localhost:5050");

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record AppActionRequest(string Id);

public record BuildStartRequest(
    string[] RepoNames,
    bool NoRestore,
    bool NoAnalyzers,
    bool NoDocs,
    bool Parallel,
    bool Snabb);
