namespace DmzCompanion.Core.Settings;

public interface ISettingsStore
{
    string? Get(string key);

    void Set(string key, string value);
}
