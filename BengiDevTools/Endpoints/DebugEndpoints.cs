using System.Text.Json;
using System.Text.RegularExpressions;
using BengiDevTools.Models;
using BengiDevTools.Services;

namespace BengiDevTools.Endpoints;

public record NewScriptRequest(string Name, string Type);
public record ExecuteSqlRequest(string Sql);

public static class DebugEndpoints
{
    public static void MapDebugEndpoints(this WebApplication app)
    {
        // ─── SQL-skript CRUD ──────────────────────────────────────────────────────

        app.MapGet("/api/debug/scripts", (ISettingsService s) =>
        {
            var root = s.Settings.DebugScriptsPath;
            if (!Directory.Exists(root)) return Results.Ok(Array.Empty<object>());

            var scripts = Directory.GetFiles(root, "*.sql", SearchOption.AllDirectories)
                .OrderBy(f => f)
                .Select(f =>
                {
                    var rel  = Path.GetRelativePath(root, f);
                    var type = rel.Split(Path.DirectorySeparatorChar)[0].ToLower() switch
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
            var name = req.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                ? req.Name : req.Name + ".sql";
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) File.WriteAllText(path, $"-- {name}\n\n");
            return Results.Ok(new { path });
        });

        app.MapDelete("/api/debug/script", (string path) =>
        {
            if (File.Exists(path)) File.Delete(path);
            return Results.Ok();
        });

        // ─── SQL-exekvering ───────────────────────────────────────────────────────

        app.MapPost("/api/debug/execute-sql", async (ExecuteSqlRequest req, ISettingsService s) =>
        {
            try
            {
                await using var conn = new Microsoft.Data.SqlClient.SqlConnection(
                    s.Settings.SqlConnectionString);
                await conn.OpenAsync();

                var results = new List<object>();
                var batches = Regex
                    .Split(req.Sql, @"^\s*GO\s*$",
                        RegexOptions.Multiline | RegexOptions.IgnoreCase)
                    .Select(b => b.Trim())
                    .Where(b => b.Length > 0);

                foreach (var batch in batches)
                {
                    await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(batch, conn);
                    cmd.CommandTimeout = 60;

                    if (batch.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                    {
                        await using var reader = await cmd.ExecuteReaderAsync();
                        var cols = Enumerable.Range(0, reader.FieldCount)
                            .Select(i => reader.GetName(i)).ToList();
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

        // ─── Swagger-proxy ────────────────────────────────────────────────────────

        app.MapGet("/api/debug/swagger", async (string appId, AppScanService scan) =>
        {
            var a = scan.GetById(appId);
            if (a?.HttpsPort is null) return Results.NotFound("App saknar HTTPS-port");

            using var http = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            });
            http.Timeout = TimeSpan.FromSeconds(5);

            string[] paths =
            [
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

            return Results.Ok(new
            {
                error = $"Ingen OpenAPI/Swagger endpoint hittades på port {a.HttpsPort}. " +
                        "Kontrollera att appen exponerar /openapi/v1.json eller /swagger/v1/swagger.json."
            });
        });

        // ─── Scenarios CRUD ───────────────────────────────────────────────────────

        app.MapGet("/api/debug/scenarios",
            (ISettingsService s) => Results.Ok(LoadScenarios(s)));

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
    }

    private static string ScenariosPath(ISettingsService s) =>
        Path.Combine(s.Settings.DebugScriptsPath, "scenarios.json");

    private static List<Scenario> LoadScenarios(ISettingsService s)
    {
        var path = ScenariosPath(s);
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<List<Scenario>>(File.ReadAllText(path)) ?? []; }
        catch { return []; }
    }

    private static void SaveScenarios(ISettingsService s, List<Scenario> list)
    {
        Directory.CreateDirectory(s.Settings.DebugScriptsPath);
        File.WriteAllText(ScenariosPath(s),
            JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}
