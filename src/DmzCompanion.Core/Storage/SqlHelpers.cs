using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DmzCompanion.Core.Storage;

internal static class SqlHelpers
{
    public static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    public static SqliteCommand Command(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params ReadOnlySpan<(string Name, object? Value)> parameters)
    {
        var command = Command(connection, transaction, sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    public static string? NullableString(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static double? NullableDouble(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    public static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidDataException($"Stored value '{value}' isn't a valid {typeof(TEnum).Name}.");

    public static string FormatTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : throw new InvalidDataException($"Stored time '{value}' isn't valid.");

    /// <summary>Prepared insert reused for many rows.</summary>
    public sealed class Insert : IDisposable
    {
        private readonly SqliteCommand _command;
        private readonly SqliteParameter[] _parameters;

        public Insert(SqliteConnection connection, SqliteTransaction transaction, string table, params string[] columns)
        {
            var names = columns.Select(c => "$" + c).ToArray();
            _command = Command(
                connection,
                transaction,
                $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", names)});");
            _parameters = names.Select(n => _command.Parameters.Add(new SqliteParameter { ParameterName = n })).ToArray();
        }

        public Task ExecAsync(CancellationToken cancellationToken, params object?[] values)
        {
            for (var i = 0; i < _parameters.Length; i++)
            {
                _parameters[i].Value = values[i] ?? DBNull.Value;
            }

            return _command.ExecuteNonQueryAsync(cancellationToken);
        }

        public void Dispose() => _command.Dispose();
    }
}
