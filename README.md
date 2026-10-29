# MW4 DMZ Companion

A companion app for MW4 DMZ players on Windows and Android. Find loot, plan runs, and track the resources you need.

## Features

- **Interactive map**: pan, zoom, layer toggles, search, and points of interest
- **Loot locations**: weapons, manuals, parts, components, and keys
- **Resource tracking**: have/need counts, goals, and progress
- **Works offline**: the loot data is cached on your device and updates automatically

## Roadmap

Status: :white_check_mark: done, :construction: in progress, :white_large_square: planned

### Phase 0: Design
- :construction: UI mockups (map, loot browser, resources, settings, first-run wizard)
- :construction: Decide on the loot data source, map art, and account/sync approach
- :white_large_square: Sign off on the mockups

### Phase 1: Foundation
- :white_large_square: .NET 10 solution with a shared core library (Windows + Android)
- :white_large_square: Logging with a log-level setting in the app
- :white_large_square: Unit tests and CI builds

### Phase 2: Loot data
- :white_large_square: Data model for maps, zones, POIs, items, and locations
- :white_large_square: Versioned, signed data package with automatic updates
- :white_large_square: Local offline cache

### Phase 3: Interactive map
- :white_large_square: Pan/zoom map with layer toggles
- :white_large_square: Location markers and detail panel
- :white_large_square: Mark items collected
- :white_large_square: Search and jump-to-location

### Phase 4: Loot locations
- :white_large_square: Weapons
- :white_large_square: Manuals
- :white_large_square: Parts and components
- :white_large_square: Loot browser with filters and show-on-map

### Phase 5: Resource tracking
- :white_large_square: Inventory have/need counts with quick +/- controls
- :white_large_square: Goals and progress tracking

### Phase 6: First release (v1.0)
- :white_large_square: First-run setup wizard
- :white_large_square: Windows installer with auto-update
- :white_large_square: Android release

### Future
- :white_large_square: Optional sign-in to sync progress between devices
- :white_large_square: Support for more maps through data updates
- :white_large_square: Screenshot import for resource counts

## Disclaimer

This is an unofficial fan project. It isn't affiliated with or endorsed by Activision. Call of Duty and related names are trademarks of their respective owners.
