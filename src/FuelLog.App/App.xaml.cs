using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using FuelLog.App.Services;
using FuelLog.App.ViewModels;
using FuelLog.Core.Storage;

namespace FuelLog.App;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FuelLog", "errors.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Русские форматы чисел и дат во всех привязках (запятая — десятичный разделитель).
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        FrameworkContentElement.LanguageProperty.OverrideMetadata(
            typeof(System.Windows.Documents.TextElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        DispatcherUnhandledException += OnUnhandledException;

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

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch (IOException)
        {
        }

        e.Handled = true;

        // Окно сообщения прокачивает очередь сообщений; если ошибка возникает при отрисовке,
        // она повторится, пока окно открыто. Второе окно поверх первого не показываем.
        if (_showingError)
            return;
        _showingError = true;
        try
        {
            MessageBox.Show(
                $"Произошла непредвиденная ошибка:\n\n{e.Exception.Message}\n\n" +
                $"Данные проекта сохраняются автоматически. Подробности записаны в журнал:\n{LogPath}",
                "FuelLog", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _showingError = false;
        }
    }

    private static bool _showingError;
}
