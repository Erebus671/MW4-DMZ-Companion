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
| `tools/DmzCompanion.PackageSigner` | Signs data packages |
| `DmzCompanion.slnx` | Full solution |
| `DmzCompanion.Core.slnf` | Core, tests and tools, builds on any OS |

The App targets `net10.0-windows10.0.19041.0` only. Android (`net10.0-android`) gets added to `TargetFrameworks` in the App project after Windows 1.0.

## Build

Run from the repo root so `global.json` applies (it selects the Microsoft.Testing.Platform test runner).

| Task | Command |
|---|---|
| Core and tests, any OS | `dotnet build DmzCompanion.Core.slnf` |
| Run tests | `dotnet test --solution DmzCompanion.Core.slnf` |
| App, Windows only | `dotnet workload install maui`, then `dotnet build src/DmzCompanion.App -f net10.0-windows10.0.19041.0` |
| Unsigned MSIX for local testing, Windows only | `dotnet publish src/DmzCompanion.App -c Release -f net10.0-windows10.0.19041.0 -p:RuntimeIdentifierOverride=win-x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false` |

## Packaging

| Item | Value |
|---|---|
| Format | MSIX, win-x64 |
| Publisher | `CN=AtraTech` |
| Signing | Self-signed. CI creates a throwaway `CN=AtraTech` cert per run; no cert or key is stored in the repo or secrets |
| CI artifact | `DmzCompanion-msix-selfsigned`: the signed MSIX plus `DmzCompanion-AtraTech.cer` |
| Install | Import the `.cer` into Local Machine > Trusted People, then open the MSIX |
| Updates | App Installer feed (`.appinstaller` on a stable HTTPS URL), Phase 7 |

## Logging

Levels: Debug, Info, Warn, Error, Critical, Fatal, set in Settings and saved in Preferences. Default is Info. Logs roll daily or at 5 MB, keeping 10 files, in `logs` under the app's local data folder (`FileSystem.AppDataDirectory`).

Fatal maps to `LogLevel.Critical`, so the Fatal and Critical levels both capture every Critical entry.

## Data package

| Item | Value |
|---|---|
| Content | Maps, labels, zones, POIs, items, spawn areas, recipes. Map images aren't included; they're fetched from GamesAtlas |
| Signing | ECDSA P-256, SHA-256. Public keys in `PackageTrustedKeys`; updates stay off while it's empty |
| Hosting | Rolling `loot-data` GitHub release: `loot-manifest.json` plus `loot-package-r{revision}.json` |
| Updates | At startup, then every 6 hours; 30 minutes after a network or storage failure. Newer revisions only. Any failure keeps the installed package |
| Limits | HTTPS only, 30 s per request, manifest 64 KB, package 32 MB |
| Private key | Never in the repo; `*.pem`, `*.key`, `*.p8` are ignored |

| Task | Command |
|---|---|
| Create a key | `dotnet run --project tools/DmzCompanion.PackageSigner -- keygen PATH.pem`, then add the printed public key to `PackageTrustedKeys` |
| Sign | `dotnet run --project tools/DmzCompanion.PackageSigner -- sign PACKAGE.json KEY.pem PACKAGE_URL loot-manifest.json` |
| Verify | `dotnet run --project tools/DmzCompanion.PackageSigner -- verify loot-manifest.json PACKAGE.json [PUBLIC_KEY]` |
