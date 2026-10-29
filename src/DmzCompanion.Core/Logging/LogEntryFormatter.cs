using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.Core.Logging;

internal static class LogEntryFormatter
{
    public static string Format(
        DateTimeOffset timestamp,
        LogLevel level,
        EventId eventId,
        string category,
        string message,
        Exception? exception)
    {
        var sb = new StringBuilder(64 + category.Length + message.Length);
        sb.Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(Label(level, eventId))
            .Append(' ')
            .Append(category)
            .Append(": ")
            .Append(message)
            .AppendLine();

        if (exception is not null)
        {
            sb.Append(exception).AppendLine();
        }

        return sb.ToString();
    }

    public static string Label(LogLevel level, EventId eventId) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => LogLevelSwitch.IsFatal(eventId) ? "FTL" : "CRT",
        _ => "???",
    };
}
