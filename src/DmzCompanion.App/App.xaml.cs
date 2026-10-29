using DmzCompanion.Core.Models;

namespace DmzCompanion.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new AppShell()) { Title = AppInfo.Name };
}
