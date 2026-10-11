using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FuelLog.Ui;

/// <summary>Перехват непредвиденных ошибок: запись в журнал и сообщение пользователю без падения программы.</summary>
public static class UiErrors
{
    private static string _logPath = string.Empty;
    private static bool _showingError;

    public static string LogPath => _logPath;

    public static void Attach(Application app, string logFileName = "errors.log")
    {
        _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FuelLog", logFileName);
        app.DispatcherUnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
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
                $"Данные сохраняются автоматически. Подробности записаны в журнал:\n{_logPath}",
                "FuelLog", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _showingError = false;
        }
    }
}
