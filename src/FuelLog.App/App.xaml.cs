using System.IO;
using System.Windows;
using FuelLog.App.Services;
using FuelLog.App.ViewModels;
using FuelLog.Core.Storage;
using FuelLog.Ui;

namespace FuelLog.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Общее оформление, русские форматы чисел и дат, перехват ошибок.
        UiResources.Install(this);
        UiErrors.Attach(this);

        var snapshots = e.Args.Length >= 3 && e.Args[0] == "--snapshots";
        // В служебном режиме снимков настройки пользователя не читаются и не меняются.
        var settings = snapshots ? AppSettings.Load(Path.Combine(e.Args[2], "settings.json")) : AppSettings.Load();
        ThemeManager.Apply(settings.Theme);

        var dialogs = new DialogService();
        var viewModel = new MainViewModel(dialogs, settings);
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;

        if (snapshots)
        {
            // Служебный режим для проверки вёрстки: снимки всех вкладок в PNG.
            SnapshotRenderer.Run(window, viewModel, projectPath: e.Args[1], outputFolder: e.Args[2], theme: e.Args.ElementAtOrDefault(3));
            return;
        }

        window.Show();

        // Двойной щелчок по файлу *.gsmproject в проводнике.
        var file = e.Args.FirstOrDefault(a => a.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase));
        if (file is not null && File.Exists(file))
            viewModel.OpenPath(file);
    }
}
