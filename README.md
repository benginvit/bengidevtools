# BengiDevTools

Ett lokalt kontrollpanel-verktyg för att hantera flera ASP.NET-mikrotjänster under utveckling. Istället för att hålla koll på ett dussin terminalfönster kan du starta, stoppa och övervaka alla tjänster från ett ställe.

## Funktioner

| Flik | Vad den gör |
|------|-------------|
| **Appar** | Starta/stoppa/starta om enskilda tjänster eller alla på en gång. Visar realtidslogg, status (kör / extern / undantag), git-branch och lokalinställningar per app. |
| **Bygge** | Kör `dotnet build` mot valfria repon parallellt med realtidsutskrift. Stödjer snabbläge (no-restore, no-analyzers). |
| **Tests** | Kör SQL-skript och Swagger-anrop mot lokala tjänster. Spara scenarier och kör dem med ett klick. |
| **Testfall** | CRUD för testdatarader (svensk skatte-/uppbördsdomän). Exportera/importera CSV. Definiera körsekvenser (SQL-steg + Swagger-anrop) per testfall. |
| **Inställningar** | Sökväg till repo-roten, SQL-anslutningssträng, sökväg till SQL-skript. |

## Kom igång

### Krav

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server (valfritt — krävs bara för SQL-körning och testfallsdata)

### Kör

```bash
dotnet run --project BengiDevTools/BengiDevTools.csproj
```

Öppnar på **http://localhost:5050**.

I VS Code: tryck **F5** (pre-build-uppgift körs automatiskt).

### Bygg

```bash
dotnet build BengiDevTools/BengiDevTools.csproj
```

### Första gången

1. Gå till **Inställningar** och ange sökväg till din repo-rot (mappen som innehåller alla dina repon).
2. Gå till **Appar** och klicka **Skanna** för att indexera alla körbara projekt.
3. Välj de appar du vill starta och klicka **Starta**.

## Konfiguration

Inställningar sparas i `%LOCALAPPDATA%\BengiDevTools\settings.json`:

```json
{
  "RepoRootPath": "C:/repos",
  "SqlConnectionString": "Server=localhost;Database=...;Integrated Security=True;",
  "DebugScriptsPath": "C:/repos/_test-tool",
  "ExcludedProjects": ["Legacy", "Archive"]
}
```

App-scan-cachen sparas i `%LOCALAPPDATA%\BengiDevTools\scan-cache.json`.

## Projektstruktur

```
BengiDevTools/
  Components/Pages/    # Blazor-sidor (AppsPage, BuildPage, TestsPage, ...)
  Endpoints/           # Minimal API-routes grupperade per domän
    AppsEndpoints.cs   # /api/apps/*  — start/stopp/scan/output/git
    BuildEndpoints.cs  # /api/repos/* och /api/build/*
    DebugEndpoints.cs  # /api/debug/* — SQL-skript, Swagger-proxy, scenarier
    SettingsEndpoints.cs # /api/settings
  Models/              # Domänmodeller (ScannedApp, TestDataRow, Scenario, ...)
  Services/            # Tjänstlager (ProcessService, BuildService, GitService, ...)
  wwwroot/app.css      # All CSS (VS Code-mörkt tema)
  Program.cs           # DI-registrering + endpoint-mappning (~30 rader)
```

## Tekniska detaljer

- **Blazor Server** med interaktiva komponenter — ingen JavaScript-bundle att bygga.
- Appar startas med `dotnet run --no-build` via `ProcessService`. Barnprocesser ärver inte BengiDevTools egna `ASPNETCORE_URLS` (port 5050) eller `ASPNETCORE_ENVIRONMENT`.
- Realtidsloggar strömmas via SSE (`/api/apps/output`) och HTTP-polling (`/api/apps/lines`) som fallback för miljöer där SSE buffreras.
- Git-status hämtas parallellt (max 4 samtidiga) via `git status --porcelain` och `git rev-parse`.
- Inga tester — integrationstest görs manuellt mot riktiga tjänster via UI:t.
