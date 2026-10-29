using DmzCompanion.Core.Settings;

namespace DmzCompanion.App.Services;

public sealed class PreferencesSettingsStore : ISettingsStore
{
    public string? Get(string key) => Preferences.Default.Get<string?>(key, null);

    public void Set(string key, string value) => Preferences.Default.Set(key, value);
}
