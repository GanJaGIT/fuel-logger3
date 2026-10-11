using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FuelLog.Core.Model;
using FuelLog.Core.Storage;

namespace FuelLog.App.Services;

/// <summary>
/// Открытый проект: файл на диске и автосохранение.
/// Любое изменение данных сохраняется через секунду бездействия — кнопка «Сохранить» не нужна.
/// </summary>
public sealed partial class ProjectSession : ObservableObject, IDisposable
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromSeconds(1);

    private readonly ChangeTracker _tracker;
    private readonly DispatcherTimer _timer;
    private DateTime _knownWriteTimeUtc;

    public ProjectSession(Project project, string filePath)
    {
        Project = project;
        FilePath = Path.GetFullPath(filePath);
        _knownWriteTimeUtc = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : default;

        _timer = new DispatcherTimer { Interval = AutosaveDelay };
        _timer.Tick += (_, _) => Save();

        _tracker = new ChangeTracker(project);
        _tracker.Changed += (_, _) =>
        {
            HasUnsavedChanges = true;
            _timer.Stop();
            _timer.Start();
        };
    }

    public Project Project { get; }
    public string FilePath { get; }
    public string FolderPath => Path.GetDirectoryName(FilePath)!;
    public string ReportsFolder => ProjectStore.ReportsFolder(FilePath);

    [ObservableProperty]
    public partial bool HasUnsavedChanges { get; private set; }

    [ObservableProperty]
    public partial string SaveStatus { get; private set; } = "Сохранено";

    [ObservableProperty]
    public partial bool HasSaveError { get; private set; }

    /// <summary>Файл изменён другой копией программы; спрашиваем, прежде чем перезаписать.</summary>
    public Func<bool>? ConfirmOverwriteExternalChange { get; set; }

    public bool Save()
    {
        _timer.Stop();
        try
        {
            if (File.Exists(FilePath) && _knownWriteTimeUtc != default
                && File.GetLastWriteTimeUtc(FilePath) != _knownWriteTimeUtc
                && ConfirmOverwriteExternalChange?.Invoke() == false)
            {
                SaveStatus = "Не сохранено: файл изменён в другом окне";
                HasSaveError = true;
                return false;
            }

            ProjectStore.Save(Project, FilePath);
            _knownWriteTimeUtc = File.GetLastWriteTimeUtc(FilePath);
            HasUnsavedChanges = false;
            HasSaveError = false;
            SaveStatus = $"Сохранено в {DateTime.Now:HH:mm:ss}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HasSaveError = true;
            SaveStatus = $"Ошибка сохранения: {ex.Message}";
            _timer.Start(); // повторим попытку
            return false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _tracker.Dispose();
    }
}
