using System.Diagnostics;
using System.IO;
using System.Windows;
using FuelLog.App.Views;
using Microsoft.Win32;

namespace FuelLog.App.Services;

/// <summary>Диалоги и оболочка Windows — отделены от ViewModel, чтобы те не зависели от окон.</summary>
public interface IDialogService
{
    void Info(string message, string title = "FuelLog");
    void Warn(string message, string title = "Внимание");
    void Error(string message, string title = "Ошибка");
    bool Confirm(string message, string title = "Подтверждение");
    string? Prompt(string title, string label, string initial = "");
    string? OpenFile(string title, string filter);
    string? SaveFile(string title, string filter, string fileName, string? folder = null);
    void OpenWithShell(string path);
    void ShowInExplorer(string path);
}

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    public void Info(string message, string title = "FuelLog") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warn(string message, string title = "Внимание") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Error(string message, string title = "Ошибка") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "Подтверждение") =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? Prompt(string title, string label, string initial = "")
    {
        var dialog = new PromptDialog(title, label, initial) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string filter, string fileName, string? folder = null)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName, OverwritePrompt = true };
        if (folder is not null && Directory.Exists(folder))
            dialog.InitialDirectory = folder;
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Error($"Не удалось открыть «{Path.GetFileName(path)}»: {ex.Message}");
        }
    }

    public void ShowInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path))
                Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch (Exception ex)
        {
            Error($"Не удалось открыть папку: {ex.Message}");
        }
    }

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
