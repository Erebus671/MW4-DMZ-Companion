# MW4 DMZ Companion

A companion app for MW4 DMZ players. Find loot, plan runs, and track the resources you need. Windows first, Android after 1.0.

## Features

- **Interactive map**: pan, zoom, layer toggles, zone and building labels, search, and points of interest
- **Loot locations**: manuals, parts, components, and keys, with spawn areas and photos of where they spawn
- **Resource tracking**: have/need counts, crafting recipes, goals, and progress
- **Works offline**: the loot data is cached on your device and updates automatically
- **No account needed**: move your progress between devices with a sync code

## Roadmap

Status: :white_check_mark: done, :construction: in progress, :white_large_square: planned

### Phase 0: Design
- :construction: UI mockups (map, loot browser, resources, settings, first-run wizard)
- :white_check_mark: Decide on the loot data source, map art, and sync approach
- :white_check_mark: Sign off on the mockups

### Phase 1: Foundation
- :white_check_mark: .NET 10 MAUI solution with a shared core library
- :white_check_mark: Logging with a log-level setting in the app
- :white_check_mark: Unit tests and CI builds

### Phase 2: Loot data
- :white_large_square: Data model for maps, zones, POIs, items, and spawn areas
- :white_large_square: Versioned, signed data package with automatic updates
- :white_large_square: Local offline cache

### Phase 3: Interactive map (Russia pending official release)
- :white_large_square: Pan/zoom map with layer toggles
- :white_large_square: Zone and building label layer
- :white_large_square: Spawn area markers and detail panel with spawn photos
- :white_large_square: Lifetime collected counts
- :white_large_square: Search and jump-to-location

### Phase 4: Loot locations
- :white_large_square: Manuals
- :white_large_square: Parts and components
- :white_large_square: Loot browser with filters and show-on-map

### Phase 5: Resource tracking
- :white_large_square: Inventory have/need counts with quick +/- controls
- :white_large_square: Crafting recipes with tracked materials on the map
- :white_large_square: Common component totals (Synthetics, Electronics, Chemicals, Metals)
- :white_large_square: Goals and progress tracking
- :white_large_square: Import counts from a saved screenshot
- :white_large_square: Always-on-top overlay (borderless windowed mode)

### Phase 6: Sync
- :white_large_square: Encrypted sync code (QR or text), no server or account

### Phase 7: First release (v1.0)
- :white_large_square: First-run setup wizard
- :white_large_square: Windows installer with auto-update

### Future
- :white_large_square: Android release on Google Play
- :white_large_square: Gunsmith page: weapon locations and custom builds
- :white_large_square: In-app loot location submissions with review
- :white_large_square: Opt-in hotkey capture of the Stash/FOB screens
- :white_large_square: Xbox Game Bar widget
- :white_large_square: Support for more maps through data updates

## Disclaimer

This is an unofficial fan project. It isn't affiliated with or endorsed by Activision. Call of Duty and related names are trademarks of their respective owners. Map imagery belongs to Activision and isn't bundled with the app.
