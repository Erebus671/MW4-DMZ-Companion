namespace DmzCompanion.Core.Storage;

/// <summary>Schema history. Append new steps; never edit a shipped one.</summary>
internal static class SchemaMigrations
{
    public static IReadOnlyList<SchemaMigration> All { get; } =
    [
        new(1, "Initial package and user state tables", V1),
    ];

    // User and map image tables have no foreign keys to package tables so they survive package replacement.
    private const string V1 = """
        CREATE TABLE package_info (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            revision INTEGER NOT NULL,
            published_utc TEXT NOT NULL,
            installed_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE maps (
            id TEXT PRIMARY KEY,
            ord INTEGER NOT NULL,
            name TEXT NOT NULL,
            image_url TEXT NOT NULL,
            reference_width INTEGER NOT NULL,
            reference_height INTEGER NOT NULL,
            meters_per_pixel REAL NOT NULL
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE map_expansions (
            id TEXT PRIMARY KEY,
            ord INTEGER NOT NULL,
            map_id TEXT NOT NULL REFERENCES maps (id),
            name TEXT NOT NULL,
            status TEXT NOT NULL,
            min_x REAL NOT NULL,
            min_y REAL NOT NULL,
            max_x REAL NOT NULL,
            max_y REAL NOT NULL,
            release_note TEXT
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE zones (
            id TEXT PRIMARY KEY,
            ord INTEGER NOT NULL,
            map_id TEXT NOT NULL REFERENCES maps (id),
            expansion_id TEXT REFERENCES map_expansions (id),
            name TEXT NOT NULL,
            min_x REAL NOT NULL,
            min_y REAL NOT NULL,
            max_x REAL NOT NULL,
            max_y REAL NOT NULL
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE points_of_interest (
            id TEXT PRIMARY KEY,
            ord INTEGER NOT NULL,
            map_id TEXT NOT NULL REFERENCES maps (id),
            expansion_id TEXT REFERENCES map_expansions (id),
            zone_id TEXT REFERENCES zones (id),
            name TEXT NOT NULL,
            kind TEXT NOT NULL,
            x REAL NOT NULL,
            y REAL NOT NULL
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE map_labels (
            id TEXT PRIMARY KEY,
            ord INTEGER NOT NULL,
            map_id TEXT NOT NULL REFERENCES maps (id),
            expansion_id TEXT REFERENCES map_expansions (id),
            kind TEXT NOT NULL,
            text TEXT NOT NULL,
            x REAL NOT NULL,
            y REAL NOT NULL,
            min_zoom REAL NOT NULL,
            max_zoom REAL
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE item_categories (
            id TEXT PRIMARY KEY,
            ord INTEGER NOT NULL,
            name TEXT NOT NULL,
            short_code TEXT NOT NULL
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE items (
            id TEXT PRIMARY KEY,
            ord INTEGER NOT NULL,
            category_id TEXT NOT NULL REFERENCES item_categories (id),
            name TEXT NOT NULL,
            rarity TEXT NOT NULL,
            value INTEGER NOT NULL,
            stack_size INTEGER NOT NULL,
            kind TEXT
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE spawn_areas (
            id TEXT PRIMARY KEY,
            ord INTEGER NOT NULL,
            item_id TEXT NOT NULL REFERENCES items (id),
            map_id TEXT NOT NULL REFERENCES maps (id),
            expansion_id TEXT REFERENCES map_expansions (id),
            zone_id TEXT REFERENCES zones (id),
            x REAL NOT NULL,
            y REAL NOT NULL,
            radius REAL NOT NULL
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE spawn_photos (
            spawn_area_id TEXT NOT NULL REFERENCES spawn_areas (id) ON DELETE CASCADE,
            ord INTEGER NOT NULL,
            kind TEXT NOT NULL,
            image_ref TEXT NOT NULL,
            caption TEXT NOT NULL,
            PRIMARY KEY (spawn_area_id, ord)
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE recipe_categories (
            id TEXT PRIMARY KEY,
            ord INTEGER NOT NULL,
            name TEXT NOT NULL
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE recipes (
            id TEXT PRIMARY KEY,
            ord INTEGER NOT NULL,
            category_id TEXT NOT NULL REFERENCES recipe_categories (id),
            name TEXT NOT NULL,
            tier INTEGER NOT NULL,
            description TEXT NOT NULL,
            cash_cost INTEGER NOT NULL
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE recipe_ingredients (
            recipe_id TEXT NOT NULL REFERENCES recipes (id) ON DELETE CASCADE,
            ord INTEGER NOT NULL,
            item_id TEXT NOT NULL REFERENCES items (id),
            quantity INTEGER NOT NULL,
            PRIMARY KEY (recipe_id, ord)
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE recipe_components (
            recipe_id TEXT NOT NULL REFERENCES recipes (id) ON DELETE CASCADE,
            ord INTEGER NOT NULL,
            component TEXT NOT NULL,
            quantity INTEGER NOT NULL,
            PRIMARY KEY (recipe_id, ord)
        ) STRICT, WITHOUT ROWID;

        CREATE INDEX ix_map_expansions_map ON map_expansions (map_id);
        CREATE INDEX ix_zones_map ON zones (map_id);
        CREATE INDEX ix_poi_map ON points_of_interest (map_id);
        CREATE INDEX ix_labels_map ON map_labels (map_id);
        CREATE INDEX ix_spawn_areas_map ON spawn_areas (map_id);
        CREATE INDEX ix_spawn_areas_item ON spawn_areas (item_id);
        CREATE INDEX ix_recipe_ingredients_item ON recipe_ingredients (item_id);

        CREATE TABLE map_images (
            map_id TEXT PRIMARY KEY,
            url TEXT NOT NULL,
            last_modified TEXT,
            last_checked_utc TEXT NOT NULL,
            width INTEGER NOT NULL CHECK (width > 0),
            height INTEGER NOT NULL CHECK (height > 0),
            needs_recalibration INTEGER NOT NULL CHECK (needs_recalibration IN (0, 1))
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE user_collected (
            item_id TEXT PRIMARY KEY,
            count INTEGER NOT NULL CHECK (count >= 0),
            updated_utc TEXT NOT NULL
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE user_inventory (
            item_id TEXT PRIMARY KEY,
            have INTEGER NOT NULL CHECK (have >= 0)
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE user_components (
            component TEXT PRIMARY KEY,
            count INTEGER NOT NULL CHECK (count >= 0)
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE user_values (
            key TEXT PRIMARY KEY,
            value INTEGER NOT NULL
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE user_tracked_recipes (
            recipe_id TEXT NOT NULL UNIQUE,
            tracked_utc TEXT NOT NULL
        ) STRICT;

        CREATE TABLE user_goals (
            id TEXT PRIMARY KEY,
            ord INTEGER NOT NULL,
            name TEXT NOT NULL,
            target TEXT NOT NULL,
            created_utc TEXT NOT NULL
        ) STRICT, WITHOUT ROWID;

        CREATE TABLE user_goal_requirements (
            goal_id TEXT NOT NULL REFERENCES user_goals (id) ON DELETE CASCADE,
            ord INTEGER NOT NULL,
            item_id TEXT NOT NULL,
            quantity INTEGER NOT NULL CHECK (quantity > 0),
            PRIMARY KEY (goal_id, ord)
        ) STRICT, WITHOUT ROWID;
        """;
}
