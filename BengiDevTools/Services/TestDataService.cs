using System.Text;
using System.Text.Json;
using BengiDevTools.Models;

namespace BengiDevTools.Services;

public class TestDataService(ISettingsService settings) : ITestDataService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private List<TestDataRow> _rows = [];

    public IReadOnlyList<TestDataRow> Rows => _rows;

    private string FilePath => Path.Combine(
        settings.Settings.DebugScriptsPath, "testfalldata.json");

    public void Load()
    {
        if (!File.Exists(FilePath)) return;
        try { _rows = JsonSerializer.Deserialize<List<TestDataRow>>(File.ReadAllText(FilePath), JsonOpts) ?? []; }
        catch { _rows = []; }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_rows, JsonOpts));
    }

    public void Add(TestDataRow row)                           { _rows.Add(row);            Save(); }
    public void Remove(TestDataRow row)                        { _rows.Remove(row);         Save(); }
    public void Replace(TestDataRow old, TestDataRow updated)  { var i = _rows.IndexOf(old); if (i >= 0) _rows[i] = updated; Save(); }
    public void Clear()                                        { _rows.Clear();             Save(); }

    public string GenerateSql(IEnumerable<int> dataSetIds)
    {
        var ids  = dataSetIds.ToHashSet();
        var rows = _rows.Where(r => ids.Contains(r.DataSetId)).ToList();
        if (rows.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("DECLARE @TestDataMPM TABLE (");
        sb.AppendLine("    DataSetId INT,");
        sb.AppendLine("    Databeskrivning NVARCHAR(255),");
        sb.AppendLine("    InbetalningsspecifikationReferenceId UNIQUEIDENTIFIER,");
        sb.AppendLine("    FordranReferenceId UNIQUEIDENTIFIER,");
        sb.AppendLine("    PersonId NVARCHAR(50),");
        sb.AppendLine("    InbetalningBetalreferens NVARCHAR(50),");
        sb.AppendLine("    FordranBetalreferens NVARCHAR(50),");
        sb.AppendLine("    SubjektPersonNrOrgNr NVARCHAR(20),");
        sb.AppendLine("    SubjektNamn NVARCHAR(255),");
        sb.AppendLine("    SubjektAdress NVARCHAR(255),");
        sb.AppendLine("    SubjektCoAdress NVARCHAR(255),");
        sb.AppendLine("    SubjektPostnummer NVARCHAR(20),");
        sb.AppendLine("    SubjektPostort NVARCHAR(100),");
        sb.AppendLine("    SubjektLand NVARCHAR(10),");
        sb.AppendLine("    SubjektUrsprung NVARCHAR(50),");
        sb.AppendLine("    Subjekttyp NVARCHAR(50),");
        sb.AppendLine("    SubjektMetadata NVARCHAR(MAX),");
        sb.AppendLine("    InbetalarePersonOrgNr NVARCHAR(20),");
        sb.AppendLine("    InbetalareNamn NVARCHAR(255),");
        sb.AppendLine("    InbetalareAdress1 NVARCHAR(255),");
        sb.AppendLine("    InbetalareAdress2 NVARCHAR(255),");
        sb.AppendLine("    InbetalarePostnummer NVARCHAR(20),");
        sb.AppendLine("    InbetalareOrt NVARCHAR(100),");
        sb.AppendLine("    InbetalareLand NVARCHAR(10),");
        sb.AppendLine("    Inbetalningskanal NVARCHAR(50),");
        sb.AppendLine("    FordranUppbordsomrade NVARCHAR(50),");
        sb.AppendLine("    InbetalningKontogrupp NVARCHAR(50),");
        sb.AppendLine("    FordranSaldo DECIMAL(18,2),");
        sb.AppendLine("    FordranGrundbelopp DECIMAL(18,2),");
        sb.AppendLine("    InbetalningBelopp DECIMAL(18,2),");
        sb.AppendLine("    FordranStatus NVARCHAR(50),");
        sb.AppendLine("    IgunSignalPost NVARCHAR(MAX),");
        sb.AppendLine("    IgunSignalResultat NVARCHAR(MAX)");
        sb.AppendLine(")");

        foreach (var r in rows)
        {
            sb.Append("INSERT INTO @TestDataMPM VALUES (");
            sb.Append($"{r.DataSetId}, ");
            sb.Append($"{Str(r.Databeskrivning)}, ");
            sb.Append("NEWID(), NEWID(), ");
            sb.Append($"{Str(r.PersonId)}, ");
            sb.Append($"{Str(r.InbetalningBetalreferens)}, ");
            sb.Append($"{Str(r.FordranBetalreferens)}, ");
            sb.Append($"{Str(r.SubjektPersonNrOrgNr)}, ");
            sb.Append($"{Str(r.SubjektNamn)}, ");
            sb.Append($"{Str(r.SubjektAdress)}, ");
            sb.Append($"{Str(r.SubjektCoAdress)}, ");
            sb.Append($"{Str(r.SubjektPostnummer)}, ");
            sb.Append($"{Str(r.SubjektPostort)}, ");
            sb.Append($"{Str(r.SubjektLand)}, ");
            sb.Append($"{Str(r.SubjektUrsprung)}, ");
            sb.Append($"{Str(r.Subjekttyp)}, ");
            sb.Append($"{Str(r.SubjektMetadata)}, ");
            sb.Append($"{Str(r.InbetalarePersonOrgNr)}, ");
            sb.Append($"{Str(r.InbetalareNamn)}, ");
            sb.Append($"{Str(r.InbetalareAdress1)}, ");
            sb.Append($"{Str(r.InbetalareAdress2)}, ");
            sb.Append($"{Str(r.InbetalarePostnummer)}, ");
            sb.Append($"{Str(r.InbetalareOrt)}, ");
            sb.Append($"{Str(r.InbetalareLand)}, ");
            sb.Append($"{Str(r.Inbetalningskanal)}, ");
            sb.Append($"{Str(r.FordranUppbordsomrade)}, ");
            sb.Append($"{Str(r.InbetalningKontogrupp)}, ");
            sb.Append($"{Num(r.FordranSaldo)}, ");
            sb.Append($"{Num(r.FordranGrundbelopp)}, ");
            sb.Append($"{Num(r.InbetalningBelopp)}, ");
            sb.Append($"{Str(r.FordranStatus)}, ");
            sb.Append($"{Str(r.IgunSignalPost)}, ");
            sb.Append($"{Str(r.IgunSignalResultat)}");
            sb.AppendLine(")");
        }

        return sb.ToString();
    }

    private static string Str(string? v) =>
        string.IsNullOrEmpty(v) ? "NULL" : $"N'{v.Replace("'", "''")}'";

    private static string Num(string? v) =>
        string.IsNullOrEmpty(v) ? "NULL" : v.Replace(",", ".");

    // ── CSV export / import ────────────────────────────────────────────────────

    private static readonly string[] CsvHeaders =
    [
        "DataSetId","Databeskrivning","PersonId","SubjektPersonNrOrgNr","SubjektNamn",
        "SubjektAdress","SubjektCoAdress","SubjektPostnummer","SubjektPostort","SubjektLand",
        "SubjektUrsprung","Subjekttyp","SubjektMetadata",
        "InbetalarePersonOrgNr","InbetalareNamn","InbetalareAdress1","InbetalareAdress2",
        "InbetalarePostnummer","InbetalareOrt","InbetalareLand",
        "InbetalningsspecifikationReferens","FordranReferens",
        "InbetalningBetalreferens","FordranBetalreferens","Inbetalningskanal",
        "FordranUppbordsomrade","InbetalningKontogrupp",
        "FordranSaldo","FordranGrundbelopp","InbetalningBelopp","FordranStatus",
        "IgunSignalPost","IgunSignalResultat"
    ];

    public string ExportCsv(IEnumerable<TestDataRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", CsvHeaders));
        foreach (var r in rows)
            sb.AppendLine(string.Join(";", GetValues(r).Select(CsvEscape)));
        return sb.ToString();
    }

    // Maps alternative column names (from the original SQL schema) to our field names
    private static readonly Dictionary<string, string> ColAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["InbetalningsspecifikationReferenceId"] = "InbetalningsspecifikationReferens",
        ["FordranReferenceId"]                   = "FordranReferens",
        // columns in source that we simply ignore (no matching field)
        ["SubjektVarde"]       = "",
        ["SubjektAttributtyp"] = "",
        ["SubjektDatatyp"]     = "",
        ["Ursprungsvaluta"]    = "",
    };

    public List<TestDataRow> ImportCsv(string csv)
    {
        var result = new List<TestDataRow>();
        var lines  = csv.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return result;

        // Auto-detect separator: count commas vs semicolons in header line
        var sep = lines[0].Count(c => c == ',') > lines[0].Count(c => c == ';') ? ',' : ';';

        var rawHeaders = ParseCsvLine(lines[0], sep);
        var headers    = rawHeaders.Select(h => ColAliases.TryGetValue(h, out var alias) ? alias : h).ToList();
        // Skip ignored columns (aliased to "") and keep first occurrence of duplicates
        var idx = headers
            .Select((h, i) => (h, i))
            .Where(x => !string.IsNullOrEmpty(x.h))
            .GroupBy(x => x.h, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);

        for (int li = 1; li < lines.Length; li++)
        {
            var cols = ParseCsvLine(lines[li], sep);
            string Get(string key)
            {
                if (!idx.TryGetValue(key, out var i) || i >= cols.Count) return "";
                var v = cols[i];
                return string.Equals(v, "NULL", StringComparison.OrdinalIgnoreCase) ? "" : v;
            }
            result.Add(new TestDataRow
            {
                DataSetId                         = int.TryParse(Get("DataSetId"), out var id) ? id : 0,
                Databeskrivning                   = Get("Databeskrivning"),
                PersonId                          = Get("PersonId"),
                SubjektPersonNrOrgNr              = Get("SubjektPersonNrOrgNr"),
                SubjektNamn                       = Get("SubjektNamn"),
                SubjektAdress                     = Get("SubjektAdress"),
                SubjektCoAdress                   = Get("SubjektCoAdress"),
                SubjektPostnummer                 = Get("SubjektPostnummer"),
                SubjektPostort                    = Get("SubjektPostort"),
                SubjektLand                       = Get("SubjektLand"),
                SubjektUrsprung                   = Get("SubjektUrsprung"),
                Subjekttyp                        = Get("Subjekttyp"),
                SubjektMetadata                   = Get("SubjektMetadata"),
                InbetalarePersonOrgNr             = Get("InbetalarePersonOrgNr"),
                InbetalareNamn                    = Get("InbetalareNamn"),
                InbetalareAdress1                 = Get("InbetalareAdress1"),
                InbetalareAdress2                 = Get("InbetalareAdress2"),
                InbetalarePostnummer              = Get("InbetalarePostnummer"),
                InbetalareOrt                     = Get("InbetalareOrt"),
                InbetalareLand                    = Get("InbetalareLand"),
                InbetalningsspecifikationReferens = Get("InbetalningsspecifikationReferens"),
                FordranReferens                   = Get("FordranReferens"),
                InbetalningBetalreferens          = Get("InbetalningBetalreferens"),
                FordranBetalreferens              = Get("FordranBetalreferens"),
                Inbetalningskanal                 = Get("Inbetalningskanal"),
                FordranUppbordsomrade             = Get("FordranUppbordsomrade"),
                InbetalningKontogrupp             = Get("InbetalningKontogrupp"),
                FordranSaldo                      = Get("FordranSaldo"),
                FordranGrundbelopp               = Get("FordranGrundbelopp"),
                InbetalningBelopp                 = Get("InbetalningBelopp"),
                FordranStatus                     = Get("FordranStatus"),
                IgunSignalPost                    = Get("IgunSignalPost"),
                IgunSignalResultat                = Get("IgunSignalResultat"),
            });
        }
        return result;
    }

    private static IEnumerable<string> GetValues(TestDataRow r) =>
    [
        r.DataSetId.ToString(), r.Databeskrivning, r.PersonId,
        r.SubjektPersonNrOrgNr, r.SubjektNamn, r.SubjektAdress, r.SubjektCoAdress,
        r.SubjektPostnummer, r.SubjektPostort, r.SubjektLand, r.SubjektUrsprung,
        r.Subjekttyp, r.SubjektMetadata,
        r.InbetalarePersonOrgNr, r.InbetalareNamn, r.InbetalareAdress1, r.InbetalareAdress2,
        r.InbetalarePostnummer, r.InbetalareOrt, r.InbetalareLand,
        r.InbetalningsspecifikationReferens, r.FordranReferens,
        r.InbetalningBetalreferens, r.FordranBetalreferens, r.Inbetalningskanal,
        r.FordranUppbordsomrade, r.InbetalningKontogrupp,
        r.FordranSaldo, r.FordranGrundbelopp, r.InbetalningBelopp, r.FordranStatus,
        r.IgunSignalPost, r.IgunSignalResultat,
    ];

    private static string CsvEscape(string v)
    {
        if (v.Contains(';') || v.Contains('"') || v.Contains('\n'))
            return $"\"{v.Replace("\"", "\"\"")}\"";
        return v;
    }

    private static List<string> ParseCsvLine(string line, char sep = ';')
    {
        var result = new List<string>();
        var sb     = new StringBuilder();
        bool inQ   = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQ)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQ = false;
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQ = true;
                else if (c == sep) { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }
}
