using DmzCompanion.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DmzCompanion.App.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // WinUI dispatcher faults bypass AppDomain.UnhandledException.
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var handler = IPlatformApplication.Current?.Services.GetService<GlobalExceptionHandler>();
        if (handler is null)
        {
            System.Diagnostics.Trace.TraceError($"Unhandled exception before startup: {e.Exception}");
            return;
        }

        handler.HandleUnhandled(e.Exception, isTerminating: !e.Handled, "WinUI");
    }
}
