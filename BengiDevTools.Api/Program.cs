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
builder.Services.AddHostedService<GitScanBackgroundService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();

app.Services.GetRequiredService<ISettingsService>().Load();
app.Services.GetRequiredService<AppScanService>().LoadCache();

var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

// ─── Settings ─────────────────────────────────────────────────────────────────

app.MapGet("/api/settings", (ISettingsService s) => s.Settings);

app.MapPut("/api/settings", (AppSettings body, ISettingsService s) =>
{
    s.Settings.RepoRootPath        = body.RepoRootPath;
    s.Settings.SqlConnectionString = body.SqlConnectionString;
    s.Settings.DebugScriptsPath    = body.DebugScriptsPath;
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

// GET = returnera cache, POST = tvinga ny scan
app.MapGet("/api/apps/scan", (AppScanService scan, IProcessService proc) =>
    MapApps(scan, proc));

app.MapPost("/api/apps/scan", (AppScanService scan, IProcessService proc) =>
{
    scan.Scan();
    return MapApps(scan, proc);
});

app.MapGet("/api/apps/scan/info", (AppScanService scan) => new
{
    count      = scan.Cached.Count,
    lastScanned = scan.LastScanned,
});

static object MapApps(AppScanService scan, IProcessService proc) =>
    scan.Cached.Select(a => new
    {
        a.Id, a.RepoName, a.ProjectName, a.HttpsPort, a.LaunchProfile,
        IsRunning    = proc.IsRunning(a.Id),
        HasLocalUser = a.HasLocalUser,
        GitStatus    = scan.GetGitStatus(a.RepoName),
        LocalhostUrl = a.HttpsPort.HasValue ? $"https://localhost:{a.HttpsPort}" : null,
    });

// Poll running status (ingen re-scan)
app.MapGet("/api/apps/status", async (AppScanService scan, IProcessService proc) =>
{
    await proc.DetectExternalAsync(scan.Cached);
    return scan.Cached.Select(a => new
    {
        a.Id,
        IsRunning    = proc.IsRunning(a.Id) || proc.IsExternal(a.Id),
        IsExternal   = proc.IsExternal(a.Id),
        HasException = proc.HasException(a.Id),
        GitStatus    = scan.GetGitStatus(a.RepoName),
    });
});

// REST poll: output lines from offset (works in codespace where SSE streaming is buffered)
app.MapGet("/api/apps/lines", (string id, int offset, IProcessService proc) =>
{
    var all = proc.GetOutputBuffer(id);
    var slice = offset < all.Count ? all.Skip(offset).ToArray() : [];
    return Results.Ok(new { lines = slice, total = all.Count });
});

// SSE: live output per app
app.MapGet("/api/apps/output", async (string id, HttpContext ctx, IProcessService proc) =>
{
    ctx.Response.Headers.ContentType  = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection   = "keep-alive";
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

    // Skicka befintlig buffer direkt
    foreach (var line in proc.GetOutputBuffer(id))
    {
        await ctx.Response.WriteAsync(
            $"data: {JsonSerializer.Serialize(line)}\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    }

    // Streama nya rader
    var channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
    proc.Subscribe(id, channel);
    try
    {
        await foreach (var line in channel.Reader.ReadAllAsync(ctx.RequestAborted))
        {
            await ctx.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(line)}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
    }
    finally { proc.Unsubscribe(id, channel); }
});

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

/// ─── Apps: localuser settings ─────────────────────────────────────────────────

app.MapGet("/api/apps/localuser", (string id, AppScanService scan) =>
{
    var a = scan.GetById(id);
    if (a is null) return Results.NotFound();
    var content = a.HasLocalUser ? File.ReadAllText(a.LocalUserPath) : null;
    return Results.Ok(new { content, path = a.LocalUserPath, exists = a.HasLocalUser });
});

app.MapPut("/api/apps/localuser", async (string id, HttpContext ctx, AppScanService scan) =>
{
    var a = scan.GetById(id);
    if (a is null) return Results.NotFound();
    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
    // Validate JSON
    try { JsonDocument.Parse(body); } catch { return Results.BadRequest("Ogiltig JSON"); }
    await File.WriteAllTextAsync(a.LocalUserPath, body);
    return Results.Ok();
});

app.MapGet("/api/apps/localuser/export", (AppScanService scan) =>
{
    var files = scan.Cached
        .Where(a => a.HasLocalUser)
        .Select(a => (a.Id, a.LocalUserPath))
        .ToList();

    if (files.Count == 0)
        return Results.NotFound("Inga localuser-filer hittades");

    var ms = new MemoryStream();
    using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var (id, path) in files)
        {
            var entryName = id.Replace('/', '_') + "_appsettings.localuser.json";
            var entry = zip.CreateEntry(entryName);
            using var entryStream = entry.Open();
            using var fileStream  = File.OpenRead(path);
            fileStream.CopyTo(entryStream);
        }
    }
    ms.Position = 0;
    return Results.File(ms, "application/zip", "appsettings-localuser.zip");
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

// ─── Debug: SQL scripts ────────────────────────────────────────────────────────

app.MapGet("/api/debug/scripts", (ISettingsService s) =>
{
    var root = s.Settings.DebugScriptsPath;
    if (!Directory.Exists(root)) return Results.Ok(Array.Empty<object>());

    var scripts = Directory.GetFiles(root, "*.sql", SearchOption.AllDirectories)
        .OrderBy(f => f)
        .Select(f =>
        {
            var rel      = Path.GetRelativePath(root, f);
            var type     = rel.Split(Path.DirectorySeparatorChar)[0].ToLower() switch
            {
                "clean" => "clean",
                "feed"  => "feed",
                _       => "other",
            };
            return new { name = Path.GetFileName(f), type, path = f, relativePath = rel };
        });

    return Results.Ok(scripts);
});

app.MapGet("/api/debug/script", (string path) =>
{
    if (!File.Exists(path)) return Results.NotFound();
    return Results.Ok(new { content = File.ReadAllText(path) });
});

app.MapPut("/api/debug/script", async (string path, HttpContext ctx) =>
{
    var dir = Path.GetDirectoryName(path)!;
    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
    await File.WriteAllTextAsync(path, body);
    return Results.Ok();
});

app.MapPost("/api/debug/scripts/new", (NewScriptRequest req, ISettingsService s) =>
{
    var dir  = Path.Combine(s.Settings.DebugScriptsPath, req.Type);
    Directory.CreateDirectory(dir);
    var name = req.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ? req.Name : req.Name + ".sql";
    var path = Path.Combine(dir, name);
    if (!File.Exists(path)) File.WriteAllText(path, $"-- {name}\n\n");
    return Results.Ok(new { path });
});

app.MapDelete("/api/debug/script", (string path) =>
{
    if (File.Exists(path)) File.Delete(path);
    return Results.Ok();
});

// ─── Debug: SQL execute ────────────────────────────────────────────────────────

app.MapPost("/api/debug/execute-sql", async (ExecuteSqlRequest req, ISettingsService s) =>
{
    try
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(s.Settings.SqlConnectionString);
        await conn.OpenAsync();

        var results = new List<object>();
        // Split on GO statements
        var batches = System.Text.RegularExpressions.Regex
            .Split(req.Sql, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(b => b.Trim())
            .Where(b => b.Length > 0);

        foreach (var batch in batches)
        {
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(batch, conn);
            cmd.CommandTimeout = 60;

            // SELECT → return rows; otherwise → rows affected
            if (batch.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                var cols = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
                var rows = new List<Dictionary<string, object?>>();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[cols[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }
                results.Add(new { type = "select", columns = cols, rows });
            }
            else
            {
                var affected = await cmd.ExecuteNonQueryAsync();
                results.Add(new { type = "nonquery", rowsAffected = affected });
            }
        }

        return Results.Ok(new { success = true, results });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { success = false, error = ex.Message });
    }
});

// ─── Debug: Swagger proxy ──────────────────────────────────────────────────────

app.MapGet("/api/debug/swagger", async (string appId, AppScanService scan) =>
{
    var a = scan.GetById(appId);
    if (a?.HttpsPort is null) return Results.NotFound("App saknar HTTPS-port");

    using var http = new HttpClient(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    });
    http.Timeout = TimeSpan.FromSeconds(5);

    // Try multiple common OpenAPI paths
    string[] paths = [
        $"https://localhost:{a.HttpsPort}/openapi/v1.json",
        $"https://localhost:{a.HttpsPort}/swagger/v1/swagger.json",
        $"https://localhost:{a.HttpsPort}/openapi/v1/openapi.json",
    ];

    foreach (var url in paths)
    {
        try
        {
            var json = await http.GetStringAsync(url);
            return Results.Content(json, "application/json");
        }
        catch { }
    }

    return Results.Ok(new { error = $"Ingen OpenAPI/Swagger endpoint hittades på port {a.HttpsPort}. Kontrollera att appen exponerar /openapi/v1.json eller /swagger/v1/swagger.json." });
});

// ─── Debug: Scenarios ──────────────────────────────────────────────────────────

string ScenariosPath(ISettingsService s) =>
    Path.Combine(s.Settings.DebugScriptsPath, "scenarios.json");

List<Scenario> LoadScenarios(ISettingsService s)
{
    var path = ScenariosPath(s);
    if (!File.Exists(path)) return [];
    try { return JsonSerializer.Deserialize<List<Scenario>>(File.ReadAllText(path)) ?? []; }
    catch { return []; }
}

void SaveScenarios(ISettingsService s, List<Scenario> list)
{
    Directory.CreateDirectory(s.Settings.DebugScriptsPath);
    File.WriteAllText(ScenariosPath(s), JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
}

app.MapGet("/api/debug/scenarios", (ISettingsService s) => Results.Ok(LoadScenarios(s)));

app.MapPut("/api/debug/scenarios", (Scenario scenario, ISettingsService s) =>
{
    var list = LoadScenarios(s);
    var idx  = list.FindIndex(x => x.Id == scenario.Id);
    if (idx >= 0) list[idx] = scenario;
    else          list.Add(scenario);
    SaveScenarios(s, list);
    return Results.Ok();
});

app.MapDelete("/api/debug/scenarios/{id}", (string id, ISettingsService s) =>
{
    var list = LoadScenarios(s).Where(x => x.Id != id).ToList();
    SaveScenarios(s, list);
    return Results.Ok();
});

app.Run("http://localhost:5050");

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record AppActionRequest(string Id);
public record NewScriptRequest(string Name, string Type);
public record ExecuteSqlRequest(string Sql);

public class Scenario
{
    public string   Id      { get; set; } = Guid.NewGuid().ToString();
    public string   Name    { get; set; } = "";
    public string   AppId   { get; set; } = "";
    public string   Method  { get; set; } = "GET";
    public string   Url     { get; set; } = "";
    public string   Body    { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();
}

public record BuildStartRequest(
    string[] RepoNames,
    bool NoRestore,
    bool NoAnalyzers,
    bool NoDocs,
    bool Parallel,
    bool Snabb);
