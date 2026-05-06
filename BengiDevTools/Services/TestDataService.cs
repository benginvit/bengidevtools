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
    public void AddRange(IEnumerable<TestDataRow> rows)        { _rows.AddRange(rows);      Save(); }
    public void Remove(TestDataRow row)                        { _rows.Remove(row);         Save(); }
    public void Replace(TestDataRow old, TestDataRow updated)  { var i = _rows.IndexOf(old); if (i >= 0) _rows[i] = updated; Save(); }
    public void Clear()                                        { _rows.Clear();             Save(); }

    public string GenerateSql(IEnumerable<int> dataSetIds)
    {
        var ids  = dataSetIds.ToHashSet();
        var rows = _rows.Where(r => ids.Contains(r.DataSetId)).ToList();
        if (rows.Count == 0) return "";

        var sb = new StringBuilder();
        sb.Append(SqlDeclarations);
        sb.AppendLine("IF OBJECT_ID('tempdb..#TestDataMPM') IS NOT NULL DROP TABLE #TestDataMPM");
        sb.AppendLine("CREATE TABLE #TestDataMPM (");
        sb.AppendLine("    DataSetId INT,");
        sb.AppendLine("    Databeskrivning NVARCHAR(255),");
        sb.AppendLine("    InbetalningsspecifikationReferenceId UNIQUEIDENTIFIER,");
        sb.AppendLine("    FordranReferenceId UNIQUEIDENTIFIER,");
        sb.AppendLine("    PersonId NVARCHAR(50),");
        sb.AppendLine("    InbetalningBetalreferens NVARCHAR(50),");
        sb.AppendLine("    FordranBetalreferens NVARCHAR(50),");
        sb.AppendLine("    POITyp NVARCHAR(50),");
        sb.AppendLine("    POIPersOrgNr NVARCHAR(20),");
        sb.AppendLine("    POIBank NVARCHAR(100),");
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
            sb.Append("INSERT INTO #TestDataMPM VALUES (");
            sb.Append($"{r.DataSetId}, ");
            sb.Append($"{Str(r.Databeskrivning)}, ");
            sb.Append("NEWID(), NEWID(), ");
            sb.Append($"{Str(r.PersonId)}, ");
            sb.Append($"{Str(r.InbetalningBetalreferens)}, ");
            sb.Append($"{Str(r.FordranBetalreferens)}, ");
            sb.Append($"{Str(r.POITyp)}, ");
            sb.Append($"{Str(r.POIPersOrgNr)}, ");
            sb.Append($"{Str(r.POIBank)}, ");
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

        sb.Append(SqlDownstreamInserts);
        return sb.ToString();
    }

    private const string SqlDeclarations =
        """
        DECLARE @DateNow AS date = FORMAT(getdate(), 'yyyyMMdd');
        DECLARE @LastMonthFirstDay AS date = FORMAT(DATEADD(MONTH, DATEDIFF(MONTH, 0, CURRENT_TIMESTAMP) - 1, 0), 'yyyyMMdd');
        DECLARE @LastMonthLastDay AS date = FORMAT(DATEADD(DAY, -1, DATEADD(MONTH, DATEDIFF(MONTH, 0, CURRENT_TIMESTAMP), 0)), 'yyyyMMdd');
        DECLARE @Tomorrow DATE = DATEADD(DAY, 1, GETDATE());
        DECLARE @Yesterday DATE = DATEADD(DAY, -1, GETDATE());
        DECLARE @TenDaysAgo DATE = DATEADD(DAY, -10, GETDATE());

        DECLARE @Inbetalningsbankgiro AS nvarchar(max) = '2505683';
        DECLARE @InbetalningsKonto AS nvarchar(max) = '5841000001009823';

        -- Inbetalningskanal
        DECLARE @Danskebank AS INT = 3;
        DECLARE @TempOutput TABLE (
        	FordranMasterId INT,
        	DataSetId BIGINT
        	);

        -- Subjekt metadata
        DECLARE @BG AS nvarchar(max) = '5051-6822';
        DECLARE @PG AS nvarchar(max) = '4803401-1';


        """;

    private const string SqlDownstreamInserts =
        """

        ----- PubliceraNyInbetalning
        INSERT INTO [USB-Support].dbo.PubliceraNyInbetalning ([InbetalningsspecifikationReferenceId], [DataSetId], [Databeskrivning], [PubliceraNyInbetalning])
        SELECT
            t.InbetalningsspecifikationReferenceId,
            t.DataSetId,
            t.Databeskrivning,
            (
        		SELECT
        			t.InbetalningsspecifikationReferenceId AS inbetalningsspecifikationReferenceId
        			FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        	) AS PubliceraNyInbetalning
        FROM #TestDataMPM t
        WHERE t.InbetalningsspecifikationReferenceId IS NOT NULL

        ----- V2_Inbetalningsspecifikation
        INSERT INTO [USB-Support].[Inbetalningsmodul].[V2_Inbetalningsspecifikation](InbetalningsspecifikationReferenceId, Belopp, Betalningsreferens, Referenskod, Inbetalningskonto, Inbetalningsbankgiro, Inbetalningsdatum, Inbetalningskanal, Meddelanden, Ursprungsbelopp, Ursprungsvaluta, Valutakurs, Inbetalningstyp, FinnsInbetalningsbild, SkapadDatum, Kontogrupp, Kallreferens)
        SELECT
            t.InbetalningsspecifikationReferenceId,
            t.InbetalningBelopp,
            t.InbetalningBetalreferens,
            2 AS Referenskod,
            @InbetalningsKonto AS Inbetalningskonto,
            @Inbetalningsbankgiro AS Inbetalningsbankgiro,
            @DateNow AS Inbetalningsdatum,
            ISNULL(t.Inbetalningskanal, 1),
            '' AS Meddelanden,
            CAST(t.InbetalningBelopp AS Decimal(18, 2)) AS Ursprungsbelopp,
        	'SEK',
            CAST(1.00 AS Decimal(18, 2)) AS Valutakurs,
            1 AS Inbetalningstyp,
            0 AS FinnsInbetalningsbild,
            @DateNow AS SkapadDatum,
            t.InbetalningKontogrupp,
        	'1' AS Kallreferens
        FROM #TestDataMPM t
        WHERE t.InbetalningsspecifikationReferenceId IS NOT NULL

        ----- V2_Inbetalare
        INSERT INTO [USB-Support].[Inbetalningsmodul].[V2_Inbetalare] ([InbetalareId],[Namn],[Adress1],[Adress2],[Postnummer],[Ort],[Land],[AvsandareBgPg],[InbetalningsspecifikationReferenceId],[InbetalareIdTyp])
        SELECT
            t.InbetalarePersonOrgNr AS InbetalareId,
            t.InbetalareNamn,
            t.InbetalareAdress1,
            t.InbetalareAdress2,
            t.InbetalarePostnummer,
            t.InbetalareOrt,
            t.InbetalareLand,
            'BG' AS AvsandareBgPg,
            t.InbetalningsspecifikationReferenceId,
        	2
        FROM #TestDataMPM t
        WHERE t.InbetalningsspecifikationReferenceId IS NOT NULL

        ----- tdl.Fordran
        INSERT INTO [USB-RedovStaging].[tdl].[Fordran](ReferenceId, FordranId, PersonId, Penningklass, Betalreferens, Ocr, Saldo, Grundbelopp, [Status], Lopnummer, Uppdateringsnummer, Fakturanummer, Registreringsnummer, Forfallodatum, Transaktionsdatum, Beslutsdatum, Ursprungstabell, SkuldId, Skapandedatum, ModifiedDateTime, Fordranskategori)
        	OUTPUT INSERTED.Id, INSERTED.FordranId INTO @TempOutput(FordranMasterId, DataSetId)
        	SELECT
        		t.FordranReferenceId AS ReferenceId,
        		t.DataSetId AS FordranId,
        		t.PersonId AS PersonId,
        		CASE
        			WHEN t.FordranUppbordsomrade = 31 THEN 'AN'
        			WHEN t.FordranUppbordsomrade = 32 THEN 'TN'
        			WHEN t.FordranUppbordsomrade = 33 THEN 'FS'
        			WHEN t.FordranUppbordsomrade = 34 THEN 'KA'
        			WHEN t.FordranUppbordsomrade = 35 THEN 'YT'
        			WHEN t.FordranUppbordsomrade = 36 THEN 'SA'
        			WHEN t.FordranUppbordsomrade = 37 THEN 'VA'
        			WHEN t.FordranUppbordsomrade = 38 THEN 'PA'
        			WHEN t.FordranUppbordsomrade = 39 THEN 'KKKK'
        			WHEN t.FordranUppbordsomrade = 40 THEN 'TA'
        			WHEN t.FordranUppbordsomrade = 41 THEN 'UK'
        			WHEN t.FordranUppbordsomrade = 42 THEN 'TS'
        			WHEN t.FordranUppbordsomrade = 43 THEN 'ODEF'
        		END AS Penningklass,
        		t.FordranBetalreferens AS Betalreferens,
        		t.FordranBetalreferens AS Ocr,
        		(t.FordranSaldo / 100) AS Saldo,
        		(t.FordranSaldo / 100) AS Grundbelopp,
        		CASE
        			WHEN t.FordranStatus = 1 THEN 1
        			WHEN t.FordranStatus = 2 THEN 2
        			WHEN t.FordranStatus = 3 THEN 3
        			ELSE 0
        		END AS [Status],
        		0 AS Lopnummer,
        		0 AS Uppdateringsnummer,
        		t.PersonId AS Fakturanummer,
        		NULL AS Registreringsnummer,
        		CASE
        			WHEN t.FordranStatus = 4 THEN @Yesterday
        			WHEN t.FordranStatus = 5 THEN @Yesterday
        			ELSE @Tomorrow
        		END AS Forfallodatum,
        		@TenDaysAgo AS Transaktionsdatum,
        		@TenDaysAgo AS Beslutsdatum,
        		CASE
        			WHEN LEFT(t.FordranBetalreferens, 2) = '10' THEN 'R92'
        			ELSE 'RP1'
        		END AS Ursprungstabell,
        		1 AS SkuldId,
        		@TenDaysAgo AS Skapandedatum,
        		@TenDaysAgo AS ModifiedDateTime,
        		CASE
        			WHEN LEFT(t.FordranBetalreferens, 2) = '10' THEN 2
        			ELSE 1
        		END AS Fordranskategori
        FROM #TestDataMPM t
        WHERE t.FordranReferenceId IS NOT NULL

        ----- tdl.SamlingsfakturaInfo
        INSERT INTO [USB-RedovStaging].[tdl].[SamlingsfakturaInfo]([Fakturanummer],[FordranId],[CreatedDateTime],[ModifiedDateTime])
        SELECT
        	t.PersonId,
        	o.FordranMasterId,
        	@TenDaysAgo AS CreatedDateTime,
        	@TenDaysAgo AS ModifiedDateTime
        FROM #TestDataMPM t
        JOIN @TempOutput o ON t.DataSetId = o.DataSetId
        JOIN [USB-RedovStaging].[tdl].[Fordran] m ON m.Id = o.FordranMasterId
        WHERE m.Fordranskategori = 1
          	AND EXISTS (
        		SELECT 1 FROM [USB-RedovStaging].[tdl].[Fordran] m2
        		WHERE m2.Fakturanummer = m.Fakturanummer AND m2.Fordranskategori = 2
        	)

        ----- tdl.Fordransartikel
        INSERT INTO [USB-RedovStaging].[tdl].[Fordransartikel] ([KallId],[FordranId],[ArtikelId],[Antal],[Artikelbelopp],[Avrakningsbelopp],[SkapadDateTime],[ModifieradDateTime],[Penningklass],[Ursprungstabell])
        SELECT
        	2 AS KallId,
        	o.FordranMasterId,
        	2 AS ArtikelId,
        	1 AS Antal,
        	(t.FordranSaldo / 100) AS Artikelbelopp,
        	0 AS Avrakningsbelopp,
        	@TenDaysAgo AS SkapadDateTime,
        	@TenDaysAgo AS ModifieradDateTime,
        	CASE
        		WHEN t.FordranUppbordsomrade = 31 THEN 'AN'
        		WHEN t.FordranUppbordsomrade = 32 THEN 'TN'
        		WHEN t.FordranUppbordsomrade = 33 THEN 'FS'
        		WHEN t.FordranUppbordsomrade = 34 THEN 'KA'
        		WHEN t.FordranUppbordsomrade = 35 THEN 'YT'
        		WHEN t.FordranUppbordsomrade = 36 THEN 'SA'
        		WHEN t.FordranUppbordsomrade = 37 THEN 'VA'
        		WHEN t.FordranUppbordsomrade = 38 THEN 'PA'
        		WHEN t.FordranUppbordsomrade = 39 THEN 'KKKK'
        		WHEN t.FordranUppbordsomrade = 40 THEN 'TA'
        		WHEN t.FordranUppbordsomrade = 41 THEN 'UK'
        		WHEN t.FordranUppbordsomrade = 42 THEN 'TS'
        		WHEN t.FordranUppbordsomrade = 43 THEN 'ODEF'
        	END AS Penningklass,
        	CASE
        		WHEN LEFT(t.FordranBetalreferens, 2) = '10' THEN 'R92'
        		ELSE 'RP1'
        	END AS Ursprungstabell
        FROM #TestDataMPM t
        JOIN @TempOutput o ON t.DataSetId = o.DataSetId
        JOIN [USB-RedovStaging].[tdl].[Fordran] m ON m.Id = o.FordranMasterId
        WHERE m.Fordranskategori = 1
          	AND EXISTS (
        		SELECT 1 FROM [USB-RedovStaging].[tdl].[Fordran] m2
        		WHERE m2.Fakturanummer = m.Fakturanummer AND m2.Fordranskategori = 2
        	)

        ----- Igun.PlaceringResponse
        INSERT INTO [USB-Support].[Igun].[PlaceringResponse]([Tabellverk],[FordranId],[Saldo],[Signalresultat],[Signalpost])
        SELECT
        	CASE
        		WHEN LEFT(t.FordranBetalreferens, 2) = '10' THEN 'R92'
        		ELSE 'RP1'
        	END AS Tabellverk,
        	t.DataSetId AS FordranId,
        	CASE
        		WHEN t.FordranStatus = 4 THEN (t.FordranSaldo / 100 + 50)
        		ELSE (t.FordranSaldo / 100)
        	END AS Saldo,
        	ISNULL(t.IgunSignalResultat, 1) AS Signalresultat,
        	ISNULL(t.IgunSignalPost, 1) AS Signalpost
        FROM #TestDataMPM t
        WHERE t.FordranReferenceId IS NOT NULL

        ----- Igun.IntressentResponse mock (POI)
        INSERT INTO [USB-Support].[Igun].[IntressentResponse]([PersonId],[PersonNrOrgNrField],[CoAdress],[Adress],[Postnummer],[Postort],[Namn],[PersonTyp])
        SELECT
        	t.PersonId AS PersonId,
        	t.POIPersOrgNr AS PersonNrOrgNrField,
        	t.InbetalareAdress2 AS CoAdress,
        	t.InbetalareAdress1 AS Adress,
        	t.InbetalarePostnummer AS Postnummer,
        	t.InbetalareOrt AS Postort,
        	t.InbetalareNamn AS Namn,
        	t.POITyp AS PersonTyp
        FROM #TestDataMPM t
        WHERE t.POITyp IS NOT NULL

        ----- Igun.BankuppgifterResponse mock bankuppgifter i POI
        INSERT INTO [USB-Support].[Igun].BankuppgifterResponse
        SELECT
        	t.PersonId AS PersonId,
        	CASE
        		WHEN LEFT(t.POIPersOrgNr, 2) IN ('19', '20') THEN RIGHT(t.POIPersOrgNr, LEN(t.POIPersOrgNr) - 2)
        		ELSE t.POIPersOrgNr
        	END AS PersonOrgnummer,
        	CASE
        		WHEN LEFT(t.POIPersOrgNr, 2) IN ('19', '20') THEN LEFT(t.POIPersOrgNr, 2)
        		ELSE NULL
        	END AS Sekel,
        	CASE
        		WHEN t.POIBank = @BG THEN @BG
        		WHEN t.POIBank = @PG THEN @PG
        	END AS Kontonummer,
        	CASE
        		WHEN t.POIBank = @BG THEN 'bg'
        		WHEN t.POIBank = @PG THEN 'pg'
        	END AS Kontotyp,
        	1 AS Resultatsignal,
        	1 AS Orsaksignal
        FROM #TestDataMPM t
        WHERE t.POIBank IS NOT NULL

        ----- Igun.SaldoInklusiveDAResponse (dröjsmålsavgift)
        INSERT INTO [USB-Support].[Igun].[SaldoInklusiveDAResponse]([Tabellverk],[Saldo],[DrojsmalAvigft],[Signal],[IdProperty])
        SELECT
        	CASE
        		WHEN LEFT(t.FordranBetalreferens, 2) = '10' THEN 'R92'
        		ELSE 'RP1'
        	END AS Tabellverk,
            (t.FordranSaldo / 100) AS Saldo,
        	CASE
        		WHEN t.FordranStatus = 4 THEN 50
        		WHEN t.FordranStatus = 5 THEN 0
        		ELSE 0
        	END AS DrojsmalAvigft,
        	1 AS Signal,
        	t.DataSetId AS IdProperty
        FROM #TestDataMPM t
        WHERE t.FordranReferenceId IS NOT NULL

        ----- Igun.SaldoResponse (saldo QRB02)
        INSERT INTO [USB-Support].[Igun].[SaldoResponse]([Tabellverk],[Id],[Saldo],[Signal])
        SELECT
        	CASE
        		WHEN LEFT(t.FordranBetalreferens, 2) = '10' THEN 'R92'
        		ELSE 'RP1'
        	END AS Tabellverk,
        	t.DataSetId AS Id,
            (t.FordranSaldo / 100) AS Saldo,
        	1 AS Signal
        FROM #TestDataMPM t
        WHERE t.FordranReferenceId IS NOT NULL;

        """;

    private static string Str(string? v) =>
        string.IsNullOrEmpty(v) ? "NULL" : $"N'{v.Replace("'", "''")}'";

    private static string Num(string? v) =>
        string.IsNullOrEmpty(v) ? "NULL" : v.Replace(",", ".");

    // ── CSV export / import ────────────────────────────────────────────────────

    private static readonly string[] CsvHeaders =
    [
        "DataSetId","Databeskrivning","PersonId","POITyp","POIPersOrgNr","POIBank","SubjektPersonNrOrgNr","SubjektNamn",
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
                POITyp                            = Get("POITyp"),
                POIPersOrgNr                      = Get("POIPersOrgNr"),
                POIBank                           = Get("POIBank"),
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
        r.POITyp, r.POIPersOrgNr, r.POIBank,
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
