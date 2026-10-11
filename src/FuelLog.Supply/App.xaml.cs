using System.IO;
using System.Windows;
using FuelLog.Core.Storage;
using FuelLog.Supply.Core.Storage;
using FuelLog.Supply.Services;
using FuelLog.Supply.ViewModels;

namespace FuelLog.Supply;

public partial class App : Application
{
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FuelLog", "supply-settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        UiResources.Install(this);
        UiErrors.Attach(this, "supply-errors.log");

        var snapshots = e.Args.Length >= 3 && e.Args[0] == "--snapshots";
        // В служебном режиме снимков настройки пользователя не читаются и не меняются.
        var settings = AppSettings.Load(snapshots ? Path.Combine(e.Args[2], "settings.json") : SettingsPath);
        ThemeManager.Apply(settings.Theme);

        var viewModel = new MainViewModel(new DialogService(), settings);
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;

        if (snapshots)
        {
            SnapshotRenderer.Run(window, viewModel, documentPath: e.Args[1], outputFolder: e.Args[2], theme: e.Args.ElementAtOrDefault(3));
            return;
        }

        window.Show();

        // Двойной щелчок по файлу *.gsmsupply в проводнике.
        var file = e.Args.FirstOrDefault(a => a.EndsWith(SupplyStore.Extension, StringComparison.OrdinalIgnoreCase));
        if (file is not null && File.Exists(file))
            viewModel.OpenPath(file);
    }
}
