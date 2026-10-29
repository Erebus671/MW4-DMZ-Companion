# Project

| Item | Value |
|---|---|
| Git repository | https://github.com/Erebus671/MW4-DMZ-Companion |
| Harbor location | harbor.gravinoc.com/dmz-companion (submission review service, after Windows 1.0) |
| Platforms | Windows (1.0), Android via Google Play (after 1.0) |
| Stack | C# / .NET 10, .NET MAUI |

## Layout

| Path | Purpose |
|---|---|
| `src/DmzCompanion.Core` | Shared logic, no UI dependencies |
| `src/DmzCompanion.App` | MAUI app (Map, Loot, Resources, Settings) |
| `tests/DmzCompanion.Core.Tests` | xUnit v3 tests for Core |
| `DmzCompanion.slnx` | Full solution |
| `DmzCompanion.Core.slnf` | Core and tests only, builds on any OS |

The App targets `net10.0-windows10.0.19041.0` only. Android (`net10.0-android`) gets added to `TargetFrameworks` in the App project after Windows 1.0.

## Build

Run from the repo root so `global.json` applies (it selects the Microsoft.Testing.Platform test runner).

| Task | Command |
|---|---|
| Core and tests, any OS | `dotnet build DmzCompanion.Core.slnf` |
| Run tests | `dotnet test --solution DmzCompanion.Core.slnf` |
| App, Windows only | `dotnet workload install maui`, then `dotnet build src/DmzCompanion.App -f net10.0-windows10.0.19041.0` |

## Logging

Levels: Debug, Info, Warn, Error, Critical, Fatal, set in Settings and saved in Preferences. Default is Info. Logs roll daily or at 5 MB, keeping 10 files, in `logs` under the app's local data folder (`FileSystem.AppDataDirectory`).

Fatal is `LogLevel.Critical` with event ID 9999 "Fatal"; at the Fatal level only those entries reach the log file.
