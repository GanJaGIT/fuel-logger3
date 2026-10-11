using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.Core.Storage;
using FuelLog.Supply.Core.Linking;
using FuelLog.Supply.Core.Model;
using FuelLog.Supply.Core.Storage;
using FuelLog.Supply.Services;

namespace FuelLog.Supply.ViewModels;

public enum MainTab
{
    Day,
    StartBalances,
    Structure,
    Transport,
    Links,
    Settings,
    Help,
}

public sealed partial class MainViewModel : ObservableObject
{
    public const string SupplyFilter = "Потребность и автономность (*.gsmsupply)|*.gsmsupply|Все файлы (*.*)|*.*";
    public const string Model1Filter = "Проект FuelLog (*.gsmproject)|*.gsmproject|Все файлы (*.*)|*.*";

    private readonly IDialogService _dialogs;
    private readonly AppSettings _appSettings;

    public MainViewModel(IDialogService dialogs, AppSettings appSettings)
    {
        _dialogs = dialogs;
        _appSettings = appSettings;
        RefreshRecent();
    }

    public IDialogService Dialogs => _dialogs;
    public AppSettings AppSettings => _appSettings;
    public ObservableCollection<RecentDocument> RecentDocuments { get; } = [];

    public string AppVersion { get; } = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? string.Empty;

    public IReadOnlyList<Feature> Features { get; } =
    [
        new("", "Документ по суткам", "Запасы, остаток, приход, расход, потери, наличие, потребность и автономность"),
        new("", "Связь с FuelLog", "Масса заправки и расход на выполнение задач — из проектов модели 1"),
        new("", "Дозаправка и подвоз", "Отметили операцию — склад и подразделение пересчитались сами"),
        new("", "Транспорт подвоза", "Загрузка запасов в транспорт БМТО и РМТО одним переключателем"),
        new("", "Отчёт в Excel", "Вкладки ДТ и АБ по установленной форме и лист операций"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(Title))]
    public partial SupplySession? Session { get; private set; }

    public bool IsOpen => Session is not null;
    public string Title => Session is null ? "Потребность и автономность — FuelLog" : $"{Session.Project.Name} — Потребность и автономность";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial DayViewModel? Day { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial StartBalancesViewModel? StartBalances { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial StructureViewModel? Structure { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial TransportViewModel? Transport { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial LinksViewModel? Links { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial SettingsViewModel? Settings { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial MainTab SelectedTab { get; set; }

    public object? CurrentPage => SelectedTab switch
    {
        MainTab.Day => Day,
        MainTab.StartBalances => StartBalances,
        MainTab.Structure => Structure,
        MainTab.Transport => Transport,
        MainTab.Links => Links,
        MainTab.Settings => Settings,
        MainTab.Help => HelpPage.Instance,
        _ => null,
    };

    partial void OnSelectedTabChanged(MainTab value)
    {
        // Проекты модели 1 могли измениться в FuelLog, пока открыт этот документ.
        Session?.RefreshModel1();
        if (value == MainTab.Structure)
            Structure?.Refresh();
        if (value == MainTab.Links)
            Links?.Refresh();
    }

    // ---- Файлы ----

    [RelayCommand]
    private void NewDocument()
    {
        var path = _dialogs.SaveFile("Создать документ «Потребность и автономность»", SupplyFilter, "Потребность и автономность.gsmsupply");
        if (path is null)
            return;

        SupplyProject project;
        string? model1Path = null;
        if (_dialogs.Confirm(
                "Привязать проект FuelLog (модель 1) сейчас?\n\n" +
                "Из него будут взяты техника подразделений (масса заправки) и расход на выполнение задач, " +
                "а структура документа будет построена по его воинским частям.\n\n" +
                "Если нажать «Нет», будет создана типовая структура (управление, 1–4 рп, трб), а проект можно привязать позже.",
                "Новый документ"))
            model1Path = _dialogs.OpenFile("Проект FuelLog (модель 1)", Model1Filter);

        if (model1Path is not null)
        {
            project = new SupplyProject { Name = Path.GetFileNameWithoutExtension(path) };
            foreach (var model in StructureBuilder.DefaultTransportCatalog())
                project.TransportCatalog.Add(model);
            var link = new LinkedProject { Path = Model1Context.StoredPath(model1Path, path) };
            project.Links.Add(link);
            var model1 = Model1Context.Load(project, path);
            if (!model1.Models[0].IsLoaded)
            {
                _dialogs.Error($"Не удалось прочитать проект FuelLog: {model1.Models[0].Error}");
                return;
            }
            // Управление — первым; остальные части по проекту модели 1.
            if (!model1.Models[0].Project!.Groups.Any(g => StructureBuilder.Normalize(g.Name).StartsWith("упр", StringComparison.Ordinal)))
            {
                var hq = StructureBuilder.Headquarters();
                project.Groups.Add(hq);
                project.MainWarehouseRowId = hq.WarehouseRowId;
            }
            StructureBuilder.Sync(project, model1);
            project.Settings.PetrolDensity = model1.Models[0].Project!.Settings.PetrolDensity;
            project.Settings.DieselDensity = model1.Models[0].Project!.Settings.DieselDensity;
        }
        else
        {
            project = StructureBuilder.CreateTypical(Path.GetFileNameWithoutExtension(path));
        }

        DayOperations.AddDay(project, DateOnly.FromDateTime(DateTime.Today));
        try
        {
            SupplyStore.Save(project, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Не удалось создать документ: {ex.Message}");
            return;
        }
        Open(project, path);
        SelectedTab = model1Path is null ? MainTab.Links : MainTab.StartBalances;
    }

    [RelayCommand]
    private void OpenDocument()
    {
        var path = _dialogs.OpenFile("Открыть документ «Потребность и автономность»", SupplyFilter);
        if (path is not null)
            OpenPath(path);
    }

    public static string SamplesFolder => Path.Combine(AppContext.BaseDirectory, "Samples");

    [RelayCommand]
    private void OpenSample()
    {
        var supply = Path.Combine(SamplesFolder, "example.gsmsupply");
        var model1 = Path.Combine(SamplesFolder, "example-model1.gsmproject");
        if (!File.Exists(supply) || !File.Exists(model1))
        {
            _dialogs.Error($"Файлы примера не найдены в папке:\n{SamplesFolder}\n\nПереустановите программу.");
            return;
        }

        // Пример копируется в «Документы» вместе с проектом FuelLog, на который он ссылается.
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FuelLog", "Пример рд");
        var target = Path.Combine(folder, "Пример рд.gsmsupply");
        try
        {
            Directory.CreateDirectory(folder);
            if (!File.Exists(target))
                File.Copy(supply, target);
            var model1Target = Path.Combine(folder, "example-model1.gsmproject");
            if (!File.Exists(model1Target))
                File.Copy(model1, model1Target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Не удалось подготовить пример: {ex.Message}");
            return;
        }
        OpenPath(target);
    }

    [RelayCommand]
    private void OpenRecent(RecentDocument? item)
    {
        if (item is null)
            return;
        if (!File.Exists(item.Path))
        {
            if (_dialogs.Confirm($"Файл не найден:\n{item.Path}\n\nУбрать его из списка?"))
                ForgetRecent(item);
            return;
        }
        OpenPath(item.Path);
    }

    [RelayCommand]
    private void ForgetRecent(RecentDocument? item)
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
        if (path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase))
        {
            _dialogs.Info("Это проект FuelLog (модель 1). Его нужно не открыть, а привязать к документу: «Модели 1» → «Привязать проект FuelLog».");
            return;
        }

        SupplyProject project;
        try
        {
            project = SupplyStore.Load(path);
        }
        catch (ProjectFormatException ex)
        {
            _dialogs.Error($"Не удалось открыть «{Path.GetFileName(path)}».\n\n{ex.Message}");
            return;
        }
        Open(project, path);
    }

    private void Open(SupplyProject project, string path)
    {
        CloseSession();
        var session = new SupplySession(project, path)
        {
            ConfirmOverwriteExternalChange = () => _dialogs.Confirm(
                "Файл был изменён вне этого окна (возможно, он открыт в другой копии программы).\n\nПерезаписать его текущими данными?",
                "Конфликт сохранения"),
        };
        Session = session;
        Day = new DayViewModel(session, _dialogs);
        StartBalances = new StartBalancesViewModel(session);
        Structure = new StructureViewModel(session, _dialogs);
        Transport = new TransportViewModel(session, _dialogs);
        Links = new LinksViewModel(session, _dialogs);
        Settings = new SettingsViewModel(session, _appSettings, _dialogs);

        _appSettings.Touch(path, project.Name);
        _appSettings.Save();
        RefreshRecent();
        SelectedTab = MainTab.Day;
        Day.OfferToday();
    }

    [RelayCommand]
    private void CloseDocument()
    {
        CloseSession();
        RefreshRecent();
    }

    public void CloseSession()
    {
        if (Session is null)
            return;
        if (Session.HasUnsavedChanges && !Session.Save())
            _dialogs.Error($"Последние изменения не сохранены: {Session.SaveStatus}");
        _appSettings.Touch(Session.FilePath, Session.Project.Name);
        _appSettings.Save();

        Day?.Dispose();
        Session.Dispose();
        Session = null;
        Day = null;
        StartBalances = null;
        Structure = null;
        Transport = null;
        Links = null;
        Settings = null;
    }

    [RelayCommand]
    private void SaveNow() => Session?.Save();

    [RelayCommand]
    private void SaveCopy()
    {
        if (Session is null)
            return;
        var path = _dialogs.SaveFile("Сохранить копию документа", SupplyFilter,
            $"{Session.Project.Name} (копия {DateTime.Now:dd.MM.yyyy}).gsmsupply", Session.FolderPath);
        if (path is null)
            return;
        try
        {
            // Пути к проектам модели 1 пересчитываются относительно нового места файла.
            var original = Session.Project.Links.Select(l => l.Path).ToList();
            foreach (var link in Session.Project.Links)
                link.Path = Model1Context.StoredPath(Model1Context.ResolvePath(link, Session.FilePath), path);
            SupplyStore.Save(Session.Project, path);
            for (var i = 0; i < original.Count; i++)
                Session.Project.Links[i].Path = original[i];
            _dialogs.Info($"Копия документа сохранена:\n{path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Не удалось сохранить копию: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowFolder()
    {
        if (Session is not null)
            _dialogs.ShowInExplorer(Session.FilePath);
    }

    [RelayCommand]
    private void ShowHelp() => SelectedTab = MainTab.Help;

    private void RefreshRecent()
    {
        RecentDocuments.Clear();
        foreach (var r in _appSettings.Recent)
            RecentDocuments.Add(new RecentDocument(r.Name, r.Path, r.LastOpened, File.Exists(r.Path)));
    }
}

public sealed record RecentDocument(string Name, string Path, DateTime LastOpened, bool Exists)
{
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}

public sealed record Feature(string Glyph, string Title, string Text);

public sealed class HelpPage
{
    public static readonly HelpPage Instance = new();
}
