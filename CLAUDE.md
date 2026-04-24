# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

BengiDevTools is a .NET 10 Blazor Server application that acts as a unified control panel for managing multiple ASP.NET microservices in a local development environment. It lets you start/stop/restart services, stream their console output in real time, run builds across repos, execute SQL scripts, and manage test data.

## Commands

```bash
# Build
dotnet build BengiDevTools/BengiDevTools.csproj

# Run (opens at https://localhost:5050)
dotnet run --project BengiDevTools/BengiDevTools.csproj

# Run with VS Code: F5 (pre-build task runs automatically, browser auto-opens)
```

There are no automated tests in this repo — integration testing is done manually through the UI against real services.

## Architecture

**Entry point:** `BengiDevTools/Program.cs` — contains all minimal API route registrations (~600 lines). Blazor pages call these HTTP endpoints rather than services directly.

**Hard-coded app registry:** `BengiDevTools/AppRegistry.cs` — defines which 25+ applications exist, their group names, ports, and repo key mappings. This is the source of truth for what the tool manages.

**Service layer** (`BengiDevTools/Services/`):
- `AppScanService` — discovers runnable projects by walking repo directories, parsing `.csproj` and `launchSettings.json`. Caches results to `%LOCALAPPDATA%\BengiDevTools\scan-cache.json`.
- `ProcessService` — spawns `dotnet run`, streams stdout/stderr into a `System.Threading.Channels` channel (bounded 500 items), detects externally-started processes by port. Windows-specific: extracts child PID from dotnet.exe parent to track the actual app process.
- `BuildService` — runs `dotnet build` across repos with up to 4 parallel builds, streams output lines via SSE.
- `GitService` — wraps git CLI for status, checkout, pull across all configured repos.
- `TestDataService` — generates SQL INSERT statements from structured test data (Swedish tax domain: subjects, payers, payments).
- `TestCaseService` — executes SQL scripts against the configured SQL Server connection and formats results.
- `SettingsService` — reads/writes `%LOCALAPPDATA%\BengiDevTools\settings.json` (repo root path, SQL connection string, excluded projects).
- `GitScanBackgroundService` — runs on startup to pre-populate git status for all repos.

**Blazor pages** (`BengiDevTools/Components/Pages/`):
- `AppsPage.razor` — start/stop/restart apps, view streaming output, git status per app.
- `BuildPage.razor` — trigger parallel builds across multiple repos with live progress.
- `TestsPage.razor` — create/run test cases with SQL scripts and scenario management.
- `TestfallDataPage.razor` — CRUD for test data rows used by test cases.
- `SettingsPage.razor` — configure repo root, SQL connection, excluded projects.

**Real-time output:** Console output streams from `ProcessService` → SSE endpoint `/api/apps/output` → `EventSource` in the browser. Each app has its own named channel keyed by app ID.

**Persistence:** All state is local — `%LOCALAPPDATA%\BengiDevTools\` for settings and scan cache. No database.

## Key Models

- `ScannedApp` — immutable record: project path, port, launch profile, repo key, group. Has a computed property for the `appsettings.localuser.json` path.
- `AppSettings` — `RepoRootPath`, `SqlConnectionString`, `DebugScriptsPath`, `ExcludedProjects`.
- `TestCase` / `TestDataRow` — test scenario metadata, SQL script, and Swedish tax domain entities.
- `BuildFlags` — feature flags for `dotnet build` optimizations (`--no-restore`, `--no-analyzers`, etc.).

## Domain Context

The managed services are Swedish tax authority microservices (prefix `USB.*`). Test data involves Swedish tax concepts: subjects (subjekt), payers (betalare), and payments (uppbord). Repo keys like `"support"` map to full repo names like `"USB.Support"` via `AppRegistry`.
