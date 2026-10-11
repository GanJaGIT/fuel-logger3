using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.Supply.Core.Linking;
using FuelLog.Supply.Core.Model;
using FuelLog.Supply.Services;

namespace FuelLog.Supply.ViewModels;

/// <summary>Вкладка «Модели 1»: привязанные проекты FuelLog.</summary>
public sealed partial class LinksViewModel : ObservableObject
{
    private readonly SupplySession _session;
    private readonly IDialogService _dialogs;

    public LinksViewModel(SupplySession session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        _session.Recalculated += (_, _) => Refresh();
        Refresh();
    }

    public SupplyProject Project => _session.Project;
    public ObservableCollection<LinkItem> Items { get; } = [];

    [ObservableProperty]
    public partial string UnlinkedRowsText { get; private set; } = string.Empty;

    public void Refresh()
    {
        Items.Clear();
        foreach (var model in _session.Model1.Models)
        {
            var rows = Project.AllRows.Count(r => r.LinkProjectId == model.Link.Id);
            Items.Add(new LinkItem(model, rows));
        }
        var unlinked = Project.AllRows.Where(r => r.Kind == RowKind.Unit && !r.IsLinked).Select(r => $"{Project.GroupOf(r)?.Name} — {r.Name}").ToList();
        UnlinkedRowsText = unlinked.Count == 0
            ? "Все подразделения документа привязаны к модели 1."
            : $"Не привязаны к модели 1 ({unlinked.Count}): {string.Join(", ", unlinked.Take(12))}{(unlinked.Count > 12 ? "…" : string.Empty)}. " +
              "У них нет массы заправки и расхода — привяжите их на вкладке «Структура и нормы» или удалите, если подразделения нет.";
    }

    [RelayCommand]
    private void AddLink()
    {
        var path = _dialogs.OpenFile("Привязать проект FuelLog (модель 1)", MainViewModel.Model1Filter);
        if (path is null)
            return;
        if (_session.Model1.Models.Any(m => string.Equals(m.FullPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
        {
            _dialogs.Info("Этот проект уже привязан.");
            return;
        }

        var link = new LinkedProject { Path = Model1Context.StoredPath(path, _session.FilePath) };
        Project.Links.Add(link);
        _session.RefreshModel1(force: true);
        var model = _session.Model1.Models.FirstOrDefault(m => m.Link == link);
        if (model is not { IsLoaded: true })
        {
            _dialogs.Error($"Проект привязан, но прочитать его не удалось: {model?.Error}");
            return;
        }

        if (_dialogs.Confirm($"Проект «{model.Project!.Name}» привязан.\n\nСопоставить структуру документа с его воинскими частями и подразделениями (по названиям)?"))
            SyncNow();
        Refresh();
    }

    [RelayCommand]
    private void Sync() => SyncNow();

    private void SyncNow()
    {
        _session.RefreshModel1(force: true);
        var result = StructureBuilder.Sync(Project, _session.Model1);
        _dialogs.Info(result.IsEmpty
            ? "Структура уже соответствует проектам FuelLog — изменений нет."
            : $"Сопоставление выполнено.\n\nСоздано частей: {result.GroupsCreated}\nДобавлено подразделений: {result.RowsCreated}\nПривязано существующих строк: {result.RowsLinked}");
        Refresh();
    }

    [RelayCommand]
    private void Reload()
    {
        _session.RefreshModel1(force: true);
        Refresh();
    }

    [RelayCommand]
    private void Remove(LinkItem? item)
    {
        if (item is null)
            return;
        if (!_dialogs.Confirm($"Отвязать проект «{item.Name}»?\n\nПривязка снимется у строк: {item.LinkedRows}. Сами строки и данные суток останутся."))
            return;
        StructureBuilder.Unlink(Project, item.Model.Link);
        _session.RefreshModel1(force: true);
        Refresh();
    }

    [RelayCommand]
    private void Relocate(LinkItem? item)
    {
        if (item is null)
            return;
        var path = _dialogs.OpenFile($"Где теперь проект «{item.Name}»?", MainViewModel.Model1Filter);
        if (path is null)
            return;
        item.Model.Link.Path = Model1Context.StoredPath(path, _session.FilePath);
        _session.RefreshModel1(force: true);
        Refresh();
    }

    [RelayCommand]
    private void OpenInFuelLog(LinkItem? item)
    {
        if (item is null || !File.Exists(item.FullPath))
            return;
        // FuelLog ставится в ту же папку; если его нет — открываем через ассоциацию файлов.
        var fuelLog = Path.Combine(AppContext.BaseDirectory, "FuelLog.exe");
        try
        {
            if (File.Exists(fuelLog))
                Process.Start(new ProcessStartInfo(fuelLog) { ArgumentList = { item.FullPath }, UseShellExecute = false });
            else
                _dialogs.OpenWithShell(item.FullPath);
        }
        catch (Exception ex)
        {
            _dialogs.Error($"Не удалось запустить FuelLog: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowInFolder(LinkItem? item)
    {
        if (item is not null)
            _dialogs.ShowInExplorer(File.Exists(item.FullPath) ? item.FullPath : Path.GetDirectoryName(item.FullPath)!);
    }
}

public sealed class LinkItem(LinkedModel model, int linkedRows)
{
    public LinkedModel Model { get; } = model;
    public int LinkedRows { get; } = linkedRows;
    public string Name => Model.Project?.Name ?? (string.IsNullOrEmpty(Model.Link.Name) ? Path.GetFileNameWithoutExtension(Model.FullPath) : Model.Link.Name);
    public string FullPath => Model.FullPath;
    public bool IsOk => Model.IsLoaded;

    public string Status
    {
        get
        {
            if (Model.Project is not { } project)
                return $"Недоступен: {Model.Error}";
            var units = project.Groups.Sum(g => g.Units.Count);
            var dates = project.Tasks.Where(t => t.IsIncluded).Select(t => t.Date).Distinct().Order().ToList();
            var period = dates.Count == 0 ? "задач в расчёте нет"
                : dates.Count == 1 ? $"задачи на {dates[0]:dd.MM.yyyy}"
                : $"задачи с {dates[0]:dd.MM.yyyy} по {dates[^1]:dd.MM.yyyy}";
            return $"Частей: {project.Groups.Count}, подразделений: {units}; {period}; привязано строк: {LinkedRows}. " +
                   $"Плотность АБ {project.Settings.PetrolDensity:0.###}, ДТ {project.Settings.DieselDensity:0.###}.";
        }
    }
}
