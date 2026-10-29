using DmzCompanion.App.Pages;
using DmzCompanion.App.Services;
using DmzCompanion.Core.Diagnostics;
using DmzCompanion.Core.Logging;
using DmzCompanion.Core.Models;
using DmzCompanion.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DmzCompanion.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var levelSwitch = new LogLevelSwitch();
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Logging
            .AddLevelSwitch(levelSwitch)
            .AddRollingFile(
                new RollingFileLoggerOptions { Directory = Path.Combine(FileSystem.AppDataDirectory, AppConstants.LogFolderName) },
                levelSwitch);
#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<ISettingsStore, PreferencesSettingsStore>();
        builder.Services.AddSingleton<LogLevelSettings>();
        builder.Services.AddSingleton(sp => new GlobalExceptionHandler(
            sp.GetRequiredService<ILogger<GlobalExceptionHandler>>(),
            sp.GetRequiredService<RollingFileLoggerProvider>().Dispose));

        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<LootPage>();
        builder.Services.AddTransient<ResourcesPage>();
        builder.Services.AddTransient<SettingsPage>();

        var app = builder.Build();
        app.Services.GetRequiredService<GlobalExceptionHandler>().Register();
        app.Services.GetRequiredService<LogLevelSettings>().Load();
        app.Services.GetRequiredService<ILogger<MauiApp>>()
            .LogInformation("{App} started; log level {Level}.", AppConstants.Name, levelSwitch.Level);
        return app;
    }
}
