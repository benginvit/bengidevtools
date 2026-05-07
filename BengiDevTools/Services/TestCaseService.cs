using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BengiDevTools.Models;
using Microsoft.Data.SqlClient;

namespace BengiDevTools.Services;

public class TestCaseService(ISettingsService settings, ITestDataService testData) : ITestCaseService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private List<TestCase> _cases = [];

    public IReadOnlyList<TestCase> Cases => _cases;

    private string FilePath => Path.Combine(
        settings.Settings.DebugScriptsPath, "testfall.json");

    public void Load()
    {
        if (!File.Exists(FilePath)) return;
        try { _cases = JsonSerializer.Deserialize<List<TestCase>>(File.ReadAllText(FilePath), JsonOpts) ?? []; }
        catch { _cases = []; }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_cases, JsonOpts));
    }

    public void Add(TestCase tc)                        { _cases.Add(tc);            Save(); }
    public void AddRange(IEnumerable<TestCase> cases)   { _cases.AddRange(cases);   Save(); }
    public void Remove(TestCase tc)                     { _cases.Remove(tc);         Save(); }
    public void Replace(TestCase old, TestCase updated) { var i = _cases.IndexOf(old); if (i >= 0) _cases[i] = updated; Save(); }

    public async Task RunAsync(IEnumerable<TestCase> cases, string connectionString, Action<string> progress, Func<TestCaseStep, CancellationToken, Task>? onBreakpoint = null, CancellationToken ct = default)
    {
        using var http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        });
        using var httpCreds = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            UseDefaultCredentials = true,
        });
        HttpClient HttpFor(TestCaseStep s) => s.UseDefaultCredentials ? httpCreds : http;

        SqlConnection? conn = null;
        async Task EnsureConnAsync()
        {
            if (conn is not null) return;
            conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
        }

        try
        {
            foreach (var tc in cases)
            {
                ct.ThrowIfCancellationRequested();
                progress($"── #{tc.DataSetId}  {tc.Beskrivning}");

                if (tc.Steps.Count == 0) { progress("  (inga steg)"); continue; }

                foreach (var step in tc.Steps)
                {
                    ct.ThrowIfCancellationRequested();
                    if (step.Breakpoint && onBreakpoint is not null)
                        await onBreakpoint(step, ct);
                    ct.ThrowIfCancellationRequested();
                    switch (step.Type)
                    {
                        case TestCaseStepType.TestfallData:
                            progress($"  [DATA] #{step.DataSetId} {step.Label}");
                            await EnsureConnAsync();
                            await RunSqlBatchesAsync(conn!, testData.GenerateSql([step.DataSetId]), progress, ct);
                            break;

                        case TestCaseStepType.Sql:
                            progress($"  [SQL] {step.Label}");
                            await EnsureConnAsync();
                            await RunSqlBatchesAsync(conn!, step.SqlScript, progress, ct);
                            break;

                        case TestCaseStepType.Swagger:
                            progress($"  [HTTP] {step.HttpMethod} {step.Url}");
                            await RunHttpCallAsync(HttpFor(step), step, progress, ct);
                            break;

                        case TestCaseStepType.Sleep:
                            progress($"  [PAUS] {step.SleepSeconds}s");
                            await Task.Delay(TimeSpan.FromSeconds(step.SleepSeconds), ct);
                            break;

                        case TestCaseStepType.SqlForeach:
                            progress($"  [FOR] {step.Label}");
                            await EnsureConnAsync();
                            await RunSqlForeachAsync(conn!, HttpFor(step), step, progress, ct);
                            break;

                        case TestCaseStepType.SqlPoll:
                            progress($"  [POLL] {step.Label}");
                            await EnsureConnAsync();
                            await RunSqlPollAsync(conn!, step, progress, ct);
                            break;
                    }
                }
            }
        }
        finally
        {
            conn?.Dispose();
        }
    }

    private static async Task RunSqlBatchesAsync(SqlConnection conn, string sql, Action<string> progress, CancellationToken ct)
    {
        var batches = sql
            .Split(["\nGO", "\r\nGO"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList();

        if (batches.Count == 0) { progress("    (ingen SQL)"); return; }

        int totalRows = 0;
        bool ok = true;
        foreach (var batch in batches)
        {
            try
            {
                using var cmd    = new SqlCommand(batch, conn) { CommandTimeout = 60 };
                using var reader = await cmd.ExecuteReaderAsync(ct);
                bool anyResults  = false;
                do
                {
                    if (!reader.HasRows) continue;
                    anyResults = true;
                    var cols   = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                    var widths = cols.Select(c => Math.Max(c.Length, 6)).ToList();
                    progress("    " + string.Join(" | ", cols.Select((c, i) => c.PadRight(widths[i]))));
                    progress("    " + string.Join("-+-", widths.Select(w => new string('-', w))));
                    int rowCount = 0;
                    while (await reader.ReadAsync(ct) && rowCount < 200)
                    {
                        var vals = Enumerable.Range(0, reader.FieldCount)
                            .Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "")
                            .Select((v, i) => (v.Length > widths[i] ? v[..(widths[i] - 1)] + "…" : v).PadRight(widths[i]));
                        progress("    " + string.Join(" | ", vals));
                        rowCount++;
                    }
                    if (rowCount == 200) progress("    … (max 200 rader visas)");
                } while (await reader.NextResultAsync(ct));

                if (!anyResults && reader.RecordsAffected > 0)
                    totalRows += reader.RecordsAffected;
            }
            catch (Exception ex)
            {
                progress($"    FEL: {ex.Message}");
                ok = false;
                break;
            }
        }
        if (ok) progress($"    OK — {totalRows} rader påverkade");
    }

    private static async Task RunSqlForeachAsync(SqlConnection conn, HttpClient http, TestCaseStep step, Action<string> progress, CancellationToken ct)
    {
        var rows = new List<Dictionary<string, string>>();
        try
        {
            using var cmd    = new SqlCommand(step.SqlScript, conn) { CommandTimeout = 60 };
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
                rows.Add(row);
            }
        }
        catch (Exception ex) { progress($"    FEL (SQL): {ex.Message}"); return; }

        int total = rows.Count;
        progress($"    {total} rader — skickar HTTP...");

        int ok = 0, fel = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row    = rows[i];
            var url    = Substitute(step.Url,  row);
            var body   = Substitute(step.Body, row);
            var rowTag = RowTag(row, i, total);
            progress($"    {rowTag} → {step.HttpMethod} {url}");
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(step.HttpMethod), url);
                if (!string.IsNullOrEmpty(body))
                    request.Content = BuildContent(body, step.ContentType);
                ApplyHeaders(request, step.HeadersRaw);
                using var response = await http.SendAsync(request, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    progress($"      ← {(int)response.StatusCode} {response.ReasonPhrase}");
                    ok++;
                }
                else
                {
                    progress($"      ← {(int)response.StatusCode} {response.ReasonPhrase}");
                    if (!string.IsNullOrWhiteSpace(responseBody))
                        progress($"      {responseBody.Trim()[..Math.Min(600, responseBody.Trim().Length)]}");
                    fel++;
                }
            }
            catch (Exception ex) { progress($"      ← FEL: {ex.Message}"); fel++; }
        }
        progress(fel == 0 ? $"    Klart: {ok}/{total} OK" : $"    Klart: {ok} OK, {fel} FEL av {total}");
    }

    private static string RowTag(Dictionary<string, string> row, int index, int total)
    {
        var pad = total.ToString().Length;
        var nr  = $"rad {(index + 1).ToString().PadLeft(pad)}/{total}";
        // Show first column that looks like an id for quick identification
        var id  = row.GetValueOrDefault("DataSetId") ?? row.GetValueOrDefault("Id") ?? row.GetValueOrDefault("FordranId");
        return id is not null ? $"{nr} (#{id})" : nr;
    }

    private static async Task RunSqlPollAsync(SqlConnection conn, TestCaseStep step, Action<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < step.PollTimeoutSeconds)
        {
            ct.ThrowIfCancellationRequested();
            var rows = new List<Dictionary<string, string>>();
            try
            {
                using var cmd    = new SqlCommand(step.SqlScript, conn) { CommandTimeout = 10 };
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
                    rows.Add(row);
                }
            }
            catch (Exception ex) { progress($"    FEL: {ex.Message}"); return; }

            int    elapsed  = (int)sw.Elapsed.TotalSeconds;
            bool   met      = step.PollCriteria switch
            {
                "RowCountGte"  => int.TryParse(step.PollCriteriaValue, out var n) && rows.Count >= n,
                "RowCountEq"   => int.TryParse(step.PollCriteriaValue, out var n) && rows.Count == n,
                "ColumnEquals" => rows.Any(r => r.TryGetValue(step.PollCriteriaColumn, out var v) &&
                                               string.Equals(v, step.PollCriteriaValue, StringComparison.OrdinalIgnoreCase)),
                _              => false,
            };
            var criteriaStr = step.PollCriteria switch
            {
                "RowCountGte"  => $"≥ {step.PollCriteriaValue} rader",
                "RowCountEq"   => $"= {step.PollCriteriaValue} rader",
                "ColumnEquals" => $"{step.PollCriteriaColumn} = {step.PollCriteriaValue}",
                _              => "",
            };
            progress($"    [{elapsed}s] {rows.Count} rad{(rows.Count == 1 ? "" : "er")} — villkor ({criteriaStr}): {(met ? "✓" : "väntar...")}");

            if (met) return;
            await Task.Delay(1000, ct);
        }
        progress($"    Timeout efter {step.PollTimeoutSeconds}s — villkor ej uppfyllt ✗");
    }

    private static void ApplyHeaders(HttpRequestMessage request, string headersRaw)
    {
        foreach (var line in headersRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sep = line.IndexOf(':');
            if (sep <= 0) continue;
            var key = line[..sep].Trim();
            var val = line[(sep + 1)..].Trim();
            if (!request.Headers.TryAddWithoutValidation(key, val))
                request.Content?.Headers.TryAddWithoutValidation(key, val);
        }
    }

    private static StringContent BuildContent(string body, string contentType)
    {
        var content = new StringContent(body.Trim(), Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return content;
    }

    private static string Substitute(string template, Dictionary<string, string> row)
    {
        var result = template;
        foreach (var (k, v) in row)
            result = result.Replace($"{{{k}}}", v, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static async Task RunHttpCallAsync(HttpClient http, TestCaseStep step, Action<string> progress, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(step.HttpMethod), step.Url);
            if (!string.IsNullOrEmpty(step.Body))
                request.Content = BuildContent(step.Body, step.ContentType);
            ApplyHeaders(request, step.HeadersRaw);
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            progress($"    {(int)response.StatusCode} {response.ReasonPhrase}");
            if (!string.IsNullOrWhiteSpace(body))
                progress(body.Length > 500 ? $"    {body[..497]}…" : $"    {body.Trim()}");
        }
        catch (Exception ex)
        {
            progress($"    FEL: {ex.Message}");
        }
    }

    public async Task<(List<string> Columns, List<Dictionary<string, string>> Rows, string? Error)> QueryAsync(
        string sql, string connectionString, CancellationToken ct = default)
    {
        var cols = new List<string>();
        var rows = new List<Dictionary<string, string>>();
        try
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            using var cmd    = new SqlCommand(sql, conn) { CommandTimeout = 15 };
            using var reader = await cmd.ExecuteReaderAsync(ct);
            cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            while (await reader.ReadAsync(ct) && rows.Count < 500)
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "";
                rows.Add(row);
            }
            return (cols, rows, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (cols, rows, ex.Message); }
    }

    public string ExportSql(IEnumerable<TestCase> cases)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- Testfall export {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        foreach (var tc in cases)
        {
            sb.AppendLine($"-- #{tc.DataSetId} [{tc.Tag}] {tc.Beskrivning}");
            foreach (var step in tc.Steps)
            {
                switch (step.Type)
                {
                    case TestCaseStepType.TestfallData:
                        sb.AppendLine($"-- Testfallsdata #{step.DataSetId}");
                        sb.AppendLine(testData.GenerateSql([step.DataSetId]).TrimEnd());
                        sb.AppendLine("GO");
                        break;
                    case TestCaseStepType.Sql:
                        sb.AppendLine($"-- {step.Label}");
                        sb.AppendLine(step.SqlScript.TrimEnd());
                        sb.AppendLine("GO");
                        break;
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
