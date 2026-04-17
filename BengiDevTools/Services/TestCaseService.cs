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

    public void Add(TestCase tc)                       { _cases.Add(tc);            Save(); }
    public void Remove(TestCase tc)                    { _cases.Remove(tc);         Save(); }
    public void Replace(TestCase old, TestCase updated) { var i = _cases.IndexOf(old); if (i >= 0) _cases[i] = updated; Save(); }

    public async Task RunAsync(IEnumerable<TestCase> cases, string connectionString, Action<string> progress, CancellationToken ct = default)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        foreach (var tc in cases)
        {
            ct.ThrowIfCancellationRequested();
            progress($"── #{tc.DataSetId}  {tc.Beskrivning}");

            var sqlPrefix = tc.DataRows.Count > 0
                ? testData.GenerateSql(tc.DataRows) + "\n"
                : "";

            var batches = (sqlPrefix + tc.Sql)
                .Split(["\nGO", "\r\nGO"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .ToList();

            if (batches.Count == 0) { progress("  (ingen SQL)"); continue; }

            int totalRows = 0;
            bool ok = true;
            foreach (var batch in batches)
            {
                var trimmed = batch.TrimStart();
                bool isSelect = trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                             || trimmed.StartsWith("WITH",   StringComparison.OrdinalIgnoreCase);
                try
                {
                    using var cmd = new SqlCommand(batch, conn) { CommandTimeout = 60 };
                    if (isSelect)
                    {
                        using var reader = await cmd.ExecuteReaderAsync(ct);
                        do
                        {
                            if (!reader.HasRows) continue;
                            var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                            progress("  " + string.Join(" | ", cols.Select(c => c.PadRight(Math.Min(c.Length + 2, 20)))));
                            progress("  " + new string('-', Math.Min(cols.Count * 22, 80)));
                            int rowCount = 0;
                            while (await reader.ReadAsync(ct) && rowCount < 100)
                            {
                                var vals = Enumerable.Range(0, reader.FieldCount)
                                    .Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "")
                                    .Select(v => v.Length > 20 ? v[..17] + "…" : v.PadRight(20));
                                progress("  " + string.Join(" | ", vals));
                                rowCount++;
                            }
                            if (rowCount == 100) progress("  … (max 100 rader visas)");
                        } while (await reader.NextResultAsync(ct));
                    }
                    else
                    {
                        var rows = await cmd.ExecuteNonQueryAsync(ct);
                        if (rows > 0) totalRows += rows;
                    }
                }
                catch (Exception ex)
                {
                    progress($"  FEL: {ex.Message}");
                    ok = false;
                    break;
                }
            }
            if (ok) progress($"  OK — {totalRows} rader påverkade");
        }
    }

    public string ExportSql(IEnumerable<TestCase> cases)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- Testfall export {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        foreach (var tc in cases)
        {
            sb.AppendLine($"-- #{tc.DataSetId} [{tc.Tag}] {tc.Beskrivning}");
            sb.AppendLine(tc.Sql.TrimEnd());
            sb.AppendLine("GO");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
