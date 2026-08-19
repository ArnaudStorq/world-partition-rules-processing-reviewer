# World Partition Rules Processing Reviewer

A WPF desktop tool to review Unreal Engine **World Partition Rules** processing from TeamCity
build logs. It replaces the manual "download `Sundance.log`, open the web analyzer, eyeball each
actor" routine with a fast, reactive desktop workflow: fetch builds, parse logs, surface
anomalies, and keep track of what a human has already reviewed.

## Features

- **TeamCity integration** – lists recent builds (last two weeks, expandable to three months) and
  downloads the `Sundance.log` artifact automatically. Logs are named with the build date and
  session (e.g. `Sundance_11August_Missions.log`).
- **Log parsing & reporting** – extracts *Applied Rules*, *Warnings/Errors*, and *Skipped* items,
  with scope filters for `HLODLayer`, `IncludeInHLOD`, `DataLayer`, and `RuntimeGrid`.
- **Anomaly detection** – compares actor assignments against an oracle built from
  `DefaultEditor.ini` (`[/Script/WorldBuildingEditor.WorldPartitionRuleSettings]`) to flag
  deviations, expected results, and known noise.
- **AI-assisted analysis (Cursor)** – generates narrative reports and assignment reviews, and can
  open a new Cursor thread pre-loaded with full context to plan a fix.
- **Human review tracking** – mark rows as read, **Approve** or **Flag** operations (single or
  multi-selection) with free-text comments. Reports are persisted as JSON and browsable in
  dedicated *Approved reports* / *Suspicious reports* tabs.
- **Polished UX** – light/dark themes, resizable/collapsible panels, pagination, and persisted
  window layout.

## Project layout

```
src/
  WPRulesReviewer.App     # WPF application (MVVM, views, view models, themes)
  WPRulesReviewer.Core    # Parsing, TeamCity client, oracle, analysis, persistence
tests/
  WPRulesReviewer.Core.Tests
tools/
  generate-icon.ps1       # Generates the application icon
```

## Requirements

- Windows
- [.NET 9 SDK](https://dotnet.microsoft.com/download) (WPF, `net9.0-windows`)
- Optional: [Cursor CLI](https://cursor.com/) (`cursor-agent`) for AI-assisted analysis

## Build & run

```powershell
dotnet build WPRulesReviewer.slnx
dotnet run --project src/WPRulesReviewer.App
```

## Tests

```powershell
dotnet test WPRulesReviewer.slnx
```

## Configuration

Open **Settings** in the app to configure:

- TeamCity server URL and access token (the token creation procedure is documented in the UI).
- The oracle source (`DefaultEditor.ini`).
- The application data folder (default `D:\WorldPartitionRules\`) where Approved/Suspicious report
  JSON files are stored.
- Page size, themes, and other preferences.

---

(c) 2026 WB Games
