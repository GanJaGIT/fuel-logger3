using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.App.Services;
using FuelLog.Core.Model;
using FuelLog.Core.Storage;

namespace FuelLog.App.ViewModels;

public enum MainTab
{
    Structure,
    Catalog,
    Tasks,
    Report,
    Settings,
    Help,
}

public sealed partial class MainViewModel : ObservableObject
{
    private const string ProjectFilter = "Проект FuelLog (*.gsmproject)|*.gsmproject|Все файлы (*.*)|*.*";

    private readonly IDialogService _dialogs;
    private readonly AppSettings _appSettings;

    public MainViewModel(IDialogService dialogs, AppSettings appSettings)
    {
        _dialogs = dialogs;
        _appSettings = appSettings;
        RefreshRecent();
    }

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    public string AppVersion { get; } =
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? string.Empty;

    /// <summary>Возможности программы для стартовой страницы.</summary>
    public IReadOnlyList<Feature> Features { get; } =
    [
        new("", "Справочник техники", "Баки и нормы расхода на марше и при работе на месте"),
        new("", "Структура", "Воинские части, подразделения и закреплённая техника"),
        new("", "Задачи по датам", "Марш, работа на месте и ожидаемые потери техники"),
        new("", "Отчёт в Excel", "По установленной форме: расход, масса заправки, заправки"),
        new("", "Без сети и базы данных", "Проект — один файл, автосохранение и резервные копии"),
    ];

    /// <summary>Пример поставляется рядом с программой; открывается его копия в «Документах».</summary>
    public static string SampleSourcePath => Path.Combine(AppContext.BaseDirectory, "Samples", "example.gsmproject");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectOpen), nameof(Title))]
    public partial ProjectSession? Session { get; private set; }

    public bool IsProjectOpen => Session is not null;

    public string Title => Session is null ? "FuelLog — учёт расхода ГСМ" : $"{Session.Project.Name} — FuelLog";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial StructureViewModel? Structure { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial CatalogViewModel? Catalog { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial TasksViewModel? Tasks { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial ReportViewModel? Report { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial SettingsViewModel? Settings { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial MainTab SelectedTab { get; set; }

    /// <summary>ViewModel открытой вкладки; представление подбирается по DataTemplate.</summary>
    public object? CurrentPage => SelectedTab switch
    {
        MainTab.Structure => Structure,
        MainTab.Catalog => Catalog,
        MainTab.Tasks => Tasks,
        MainTab.Report => Report,
        MainTab.Settings => Settings,
        MainTab.Help => HelpPage.Instance,
        _ => null,
    };

    public AppSettings AppSettings => _appSettings;

    partial void OnSelectedTabChanged(MainTab value)
    {
        switch (value)
        {
            case MainTab.Structure:
                Structure?.Refresh();
                break;
            case MainTab.Tasks:
                Tasks?.Refresh();
                break;
            case MainTab.Report:
                Report?.Refresh();
                break;
        }
    }

    [RelayCommand]
    private void NewProject()
    {
        var path = _dialogs.SaveFile("Создать проект", ProjectFilter, "Новый проект.gsmproject");
        if (path is null)
            return;

        try
        {
            var project = Project.CreateNew(Path.GetFileNameWithoutExtension(path));
            ProjectStore.Save(project, path);
            Open(project, path);
            SelectedTab = MainTab.Catalog;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Не удалось создать проект: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenProject()
    {
        var path = _dialogs.OpenFile("Открыть проект", ProjectFilter);
        if (path is not null)
            OpenPath(path);
    }

    [RelayCommand]
    private void OpenSample()
    {
        if (!File.Exists(SampleSourcePath))
        {
            _dialogs.Error($"Файл примера не найден:\n{SampleSourcePath}\n\nПереустановите программу.");
            return;
        }

        // Пример копируется в «Документы», чтобы его можно было менять, не трогая оригинал.
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FuelLog", "Пример");
        var target = Path.Combine(folder, "Пример.gsmproject");
        try
        {
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(folder);
                File.Copy(SampleSourcePath, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Не удалось подготовить пример: {ex.Message}");
            return;
        }
        OpenPath(target);
    }

    [RelayCommand]
    private void OpenRecent(RecentProjectItem? item)
    {
        if (item is null)
            return;
        if (!File.Exists(item.Path))
        {
            if (_dialogs.Confirm($"Файл не найден:\n{item.Path}\n\nУбрать его из списка последних проектов?"))
            {
                _appSettings.Forget(item.Path);
                _appSettings.Save();
                RefreshRecent();
            }
            return;
        }
        OpenPath(item.Path);
    }

    [RelayCommand]
    private void ForgetRecent(RecentProjectItem? item)
    {
        if (item is null)
            return;
        _appSettings.Forget(item.Path);
        _appSettings.Save();
        RefreshRecent();
    }

    public void OpenPath(string path)
    {
        if (Session is not null && string.Equals(Session.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            return;

        ProjectLoadResult result;
        try
        {
            result = ProjectStore.Load(path);
        }
        catch (ProjectFormatException ex)
        {
            _dialogs.Error($"Не удалось открыть проект «{Path.GetFileName(path)}».\n\n{ex.Message}");
            return;
        }

        if (result.WasLegacy)
        {
            // Оригинал 3.x сохраняем рядом: новый формат прежняя версия прочитать не сможет.
            var backup = path + ".v3.bak";
            try
            {
                File.Copy(path, backup, overwrite: true);
                ProjectStore.Save(result.Project, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _dialogs.Error($"Не удалось преобразовать проект: {ex.Message}");
                return;
            }
            _dialogs.Info(
                "Проект создан в FuelLog 3 и преобразован в новый формат.\n\n" +
                $"Перенесено: марок — {result.Project.Catalog.Count}, подразделений — {result.Project.AllUnits.Count()}.\n" +
                "Задачи прежняя версия не сохраняла, их нужно создать заново.\n\n" +
                $"Исходный файл сохранён как «{Path.GetFileName(backup)}».",
                "Импорт проекта FuelLog 3");
        }

        Open(result.Project, path);
    }

    private void Open(Project project, string path)
    {
        CloseSession();

        var session = new ProjectSession(project, path)
        {
            ConfirmOverwriteExternalChange = () => _dialogs.Confirm(
                "Файл проекта был изменён вне этого окна (возможно, проект открыт в другой копии программы).\n\n" +
                "Перезаписать его текущими данными?", "Конфликт сохранения"),
        };

        Session = session;
        Structure = new StructureViewModel(project, _dialogs, _appSettings);
        Catalog = new CatalogViewModel(project, _dialogs);
        Tasks = new TasksViewModel(project, _dialogs);
        Report = new ReportViewModel(session, _dialogs);
        Settings = new SettingsViewModel(session, _appSettings, _dialogs);

        _appSettings.Touch(path, project.Name);
        _appSettings.Save();
        RefreshRecent();

        SelectedTab = project.Catalog.Count == 0 ? MainTab.Catalog : MainTab.Tasks;
        OnSelectedTabChanged(SelectedTab);
    }

    [RelayCommand]
    private void CloseProject()
    {
        CloseSession();
        RefreshRecent();
    }

    /// <summary>Сохраняет несохранённое и закрывает проект. Вызывается и при выходе из программы.</summary>
    public void CloseSession()
    {
        if (Session is null)
            return;

        if (Session.HasUnsavedChanges && !Session.Save())
            _dialogs.Error($"Последние изменения не сохранены: {Session.SaveStatus}");

        _appSettings.Touch(Session.FilePath, Session.Project.Name);
        _appSettings.Save();

        Session.Dispose();
        Session = null;
        Structure = null;
        Catalog = null;
        Tasks = null;
        Report = null;
        Settings = null;
    }

    [RelayCommand]
    private void SaveNow() => Session?.Save();

    [RelayCommand]
    private void SaveCopy()
    {
        if (Session is null)
            return;
        var path = _dialogs.SaveFile("Сохранить копию проекта", ProjectFilter,
            $"{Session.Project.Name} (копия {DateTime.Now:dd.MM.yyyy}).gsmproject");
        if (path is null)
            return;
        try
        {
            ProjectStore.Save(Session.Project, path);
            _dialogs.Info($"Копия проекта сохранена:\n{path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Не удалось сохранить копию: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowProjectFolder()
    {
        if (Session is not null)
            _dialogs.ShowInExplorer(Session.FilePath);
    }

    [RelayCommand]
    private void ShowHelp() => SelectedTab = MainTab.Help;

    private void RefreshRecent()
    {
        RecentProjects.Clear();
        foreach (var r in _appSettings.Recent)
            RecentProjects.Add(new RecentProjectItem(r.Name, r.Path, r.LastOpened, File.Exists(r.Path)));
    }
}

public sealed record Feature(string Glyph, string Title, string Text);

/// <summary>Маркер вкладки «Справка» (статическое содержимое).</summary>
public sealed class HelpPage
{
    public static readonly HelpPage Instance = new();
}

public sealed record RecentProjectItem(string Name, string Path, DateTime LastOpened, bool Exists)
{
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}
