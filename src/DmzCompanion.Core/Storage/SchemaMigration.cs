namespace DmzCompanion.Core.Storage;

/// <summary>One forward-only schema step. Versions start at 1 and increase by 1.</summary>
internal sealed record SchemaMigration(int Version, string Name, string Sql);
