using System.Text.Json;
using System.Threading.Channels;
using BengiDevTools.Services;

namespace BengiDevTools.Endpoints;

public record AppActionRequest(string Id);

public static class AppsEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void MapAppsEndpoints(this WebApplication app)
    {
        // ─── Scan ─────────────────────────────────────────────────────────────────

        app.MapGet("/api/apps/scan", (AppScanService scan, IProcessService proc) =>
            MapApps(scan, proc));

        app.MapPost("/api/apps/scan", (AppScanService scan, IProcessService proc) =>
        {
            scan.Scan();
            return MapApps(scan, proc);
        });

        app.MapGet("/api/apps/scan/info", (AppScanService scan) => new
        {
            count       = scan.Cached.Count,
            lastScanned = scan.LastScanned,
        });

        app.MapGet("/api/apps/detect-diag", async (AppScanService scan, IProcessService proc) =>
        {
            await proc.DetectExternalAsync(scan.Cached);
            return proc.GetDetectionDiagnostics();
        });

        // ─── Status ───────────────────────────────────────────────────────────────

        app.MapGet("/api/apps/status", async (AppScanService scan, IProcessService proc) =>
        {
            await proc.DetectExternalAsync(scan.Cached);
            return scan.Cached.Select(a => new
            {
                a.Id,
                IsRunning    = proc.IsRunning(a.Id) || proc.IsExternal(a.Id),
                IsExternal   = proc.IsExternal(a.Id),
                Pid          = proc.GetPid(a.Id),
                HasException = proc.HasException(a.Id),
                GitStatus    = scan.GetGitStatus(a.RepoName),
                GitBranch    = scan.GetGitBranch(a.RepoName),
            });
        });

        // ─── Output (poll + SSE) ──────────────────────────────────────────────────

        app.MapGet("/api/apps/lines", (string id, int offset, IProcessService proc) =>
        {
            var all   = proc.GetOutputBuffer(id);
            var slice = offset < all.Count ? all.Skip(offset).ToArray() : [];
            return Results.Ok(new { lines = slice, total = all.Count });
        });

        app.MapGet("/api/apps/output", async (string id, HttpContext ctx, IProcessService proc) =>
        {
            ctx.Response.Headers.ContentType  = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection   = "keep-alive";
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

            foreach (var line in proc.GetOutputBuffer(id))
            {
                await ctx.Response.WriteAsync(
                    $"data: {JsonSerializer.Serialize(line)}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }

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

        // ─── Start / stop / restart ───────────────────────────────────────────────

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

        // ─── Localuser settings ───────────────────────────────────────────────────

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
            using (var zip = new System.IO.Compression.ZipArchive(
                ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (id, path) in files)
                {
                    var entryName = id.Replace('/', '_') + "_appsettings.localuser.json";
                    var entry     = zip.CreateEntry(entryName);
                    using var entryStream = entry.Open();
                    using var fileStream  = File.OpenRead(path);
                    fileStream.CopyTo(entryStream);
                }
            }
            ms.Position = 0;
            return Results.File(ms, "application/zip", "appsettings-localuser.zip");
        });

        // ─── Git status (SSE) ─────────────────────────────────────────────────────

        app.MapGet("/api/apps/git-refresh",
            async (HttpContext ctx, AppScanService scan, IGitService git, ISettingsService s) =>
        {
            ctx.Response.Headers.ContentType  = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection   = "keep-alive";
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

            var root        = s.Settings.RepoRootPath;
            var channel     = Channel.CreateUnbounded<string>();
            var sem         = new SemaphoreSlim(4);
            var uniqueRepos = scan.Cached.Select(a => a.RepoName).Distinct().ToList();

            var fetchTasks = uniqueRepos.Select(async repoName =>
            {
                await sem.WaitAsync(ctx.RequestAborted);
                try
                {
                    var (status, branch) = await git.GetStatusAsync(
                        Path.Combine(root, repoName), ctx.RequestAborted);
                    scan.SetGitStatus(repoName, status, branch);
                    channel.Writer.TryWrite(
                        JsonSerializer.Serialize(new { repoName, status, branch }, JsonOpts));
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

        // ─── Git checkout default + pull (SSE) ───────────────────────────────────

        app.MapGet("/api/apps/git-checkout-all",
            async (HttpContext ctx, AppScanService scan, IGitService git, ISettingsService s) =>
        {
            ctx.Response.Headers.ContentType  = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection   = "keep-alive";
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

            var root        = s.Settings.RepoRootPath;
            var channel     = Channel.CreateUnbounded<string>();
            var sem         = new SemaphoreSlim(2);
            var uniqueRepos = scan.Cached.Select(a => a.RepoName).Distinct().ToList();

            var tasks = uniqueRepos.Select(async repoName =>
            {
                await sem.WaitAsync(ctx.RequestAborted);
                try
                {
                    var (branch, message) = await git.CheckoutDefaultAndPullAsync(
                        Path.Combine(root, repoName), ctx.RequestAborted);
                    channel.Writer.TryWrite(
                        JsonSerializer.Serialize(new { repoName, branch, message }, JsonOpts));
                }
                finally { sem.Release(); }
            });

            _ = Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.Complete());

            await foreach (var msg in channel.Reader.ReadAllAsync(ctx.RequestAborted))
            {
                await ctx.Response.WriteAsync($"data: {msg}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }

            await ctx.Response.WriteAsync("event: done\ndata: {}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        });
    }

    private static object MapApps(AppScanService scan, IProcessService proc) =>
        scan.Cached.Select(a => new
        {
            a.Id, a.RepoName, a.ProjectName, a.HttpsPort, a.LaunchProfile,
            IsRunning    = proc.IsRunning(a.Id),
            HasLocalUser = a.HasLocalUser,
            GitStatus    = scan.GetGitStatus(a.RepoName),
            GitBranch    = scan.GetGitBranch(a.RepoName),
            LocalhostUrl = a.HttpsPort.HasValue ? $"https://localhost:{a.HttpsPort}" : null,
        });
}
