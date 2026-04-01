using System.Text.Json;
using System.Threading.Channels;
using BengiDevTools;
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

// ─── Repos ────────────────────────────────────────────────────────────────────

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

// ─── Apps ─────────────────────────────────────────────────────────────────────

app.MapGet("/api/apps", (IProcessService proc) =>
    AppRegistry.Apps.Select(a => new
    {
        a.Name, a.Port, a.Group, a.RepoKey, a.ProjectName,
        IsRunning = proc.IsRunning(a.Name),
        GitStatus = proc.GetGitStatus(a.Name),
        LocalhostUrl = $"https://localhost:{a.Port}",
    }));

app.MapPost("/api/apps/{name}/start", async (string name, IProcessService proc, ISettingsService s) =>
{
    var entry = AppRegistry.Apps.FirstOrDefault(a => a.Name == name);
    if (entry is null) return Results.NotFound();
    await proc.StartAsync(entry, s.Settings.RepoRootPath);
    return Results.Ok();
});

app.MapPost("/api/apps/{name}/stop", async (string name, IProcessService proc) =>
{
    await proc.StopAsync(name);
    return Results.Ok();
});

app.MapPost("/api/apps/{name}/restart", async (string name, IProcessService proc, ISettingsService s) =>
{
    var entry = AppRegistry.Apps.FirstOrDefault(a => a.Name == name);
    if (entry is null) return Results.NotFound();
    await proc.RestartAsync(entry, s.Settings.RepoRootPath);
    return Results.Ok();
});

app.MapPost("/api/apps/start-all", async (IProcessService proc, ISettingsService s) =>
{
    foreach (var entry in AppRegistry.Apps)
        await proc.StartAsync(entry, s.Settings.RepoRootPath);
    return Results.Ok();
});

app.MapPost("/api/apps/stop-all", async (IProcessService proc) =>
{
    foreach (var entry in AppRegistry.Apps.Where(a => proc.IsRunning(a.Name)))
        await proc.StopAsync(entry.Name);
    return Results.Ok();
});

// SSE: git status refresh – streamas per repo allt eftersom de slutförs
app.MapGet("/api/apps/git-refresh", async (HttpContext ctx, IProcessService proc, IGitService git, ISettingsService s) =>
{
    ctx.Response.Headers.ContentType  = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection   = "keep-alive";
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

    var root    = s.Settings.RepoRootPath;
    var channel = Channel.CreateUnbounded<string>();
    var sem     = new SemaphoreSlim(4);

    var fetchTasks = AppRegistry.Apps.Select(async entry =>
    {
        await sem.WaitAsync(ctx.RequestAborted);
        try
        {
            if (!AppRegistry.RepoMap.TryGetValue(entry.RepoKey, out var folder)) return;
            var repoPath = Path.Combine(root, folder);
            var status   = await git.GetStatusAsync(repoPath, ctx.RequestAborted);
            proc.SetGitStatus(entry.Name, status);
            channel.Writer.TryWrite(JsonSerializer.Serialize(
                new { name = entry.Name, status }, jsonOpts));
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

    static string? FindSlnForBuild(string dir)
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
            var sln = FindSlnForBuild(dir);
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
                onProgress: (repo, status) => channel.Writer.TryWrite(
                    JsonSerializer.Serialize(new { type = "progress", repo, status }, jsonOpts)),
                onOutputLine: (repo, line) => channel.Writer.TryWrite(
                    JsonSerializer.Serialize(new { type = "output", repo, line }, jsonOpts)),
                ctx.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            channel.Writer.TryWrite(
                JsonSerializer.Serialize(new { type = "output", line = "⛔ Bygge avbrutet." }, jsonOpts));
        }
        finally
        {
            channel.Writer.Complete();
        }
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

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public record BuildStartRequest(
    string[] RepoNames,
    bool NoRestore,
    bool NoAnalyzers,
    bool NoDocs,
    bool Parallel,
    bool Snabb);
