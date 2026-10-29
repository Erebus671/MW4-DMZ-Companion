using DmzCompanion.Core.Logging;
using DmzCompanion.Core.Settings;

namespace DmzCompanion.App.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly LogLevelSettings _logLevelSettings;
    private bool _loading;

    public SettingsPage(LogLevelSettings logLevelSettings)
    {
        _logLevelSettings = logLevelSettings;
        InitializeComponent();

        _loading = true;
        LogLevelPicker.ItemsSource = Enum.GetValues<AppLogLevel>();
        LogLevelPicker.SelectedItem = logLevelSettings.Current;
        _loading = false;
    }

    private async void OnLogLevelChanged(object? sender, EventArgs e)
    {
        if (_loading || LogLevelPicker.SelectedItem is not AppLogLevel level)
        {
            return;
        }

        if (!_logLevelSettings.Save(level))
        {
            await DisplayAlertAsync("Settings", "Couldn't save the log level. It applies until the app closes.", "OK");
        }
    }
}
