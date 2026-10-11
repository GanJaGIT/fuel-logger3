using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.Core.Model;
using FuelLog.Supply.Core.Linking;
using FuelLog.Supply.Core.Model;
using FuelLog.Supply.Services;

namespace FuelLog.Supply.ViewModels;

/// <summary>Вкладка «Структура и нормы»: части, подразделения, склады, привязка к модели 1 и установленные запасы.</summary>
public sealed partial class StructureViewModel : ObservableObject
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private readonly SupplySession _session;
    private readonly IDialogService _dialogs;

    public StructureViewModel(SupplySession session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        _session.Recalculated += (_, _) => UpdatePreview();
    }

    public SupplyProject Project => _session.Project;
    public ObservableCollection<SupplyGroup> Groups => Project.Groups;

    public IReadOnlyList<ScopeOption> Scopes { get; } =
    [
        new(NormScope.Own, "своей техники"),
        new(NormScope.Group, "своей воинской части"),
        new(NormScope.Division, "всей рд"),
        new(NormScope.SelectedGroups, "выбранных частей"),
    ];

    /// <summary>Выделенный узел: <see cref="SupplyGroup"/> или <see cref="SupplyRow"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedGroup), nameof(SelectedRow), nameof(IsGroupSelected), nameof(IsRowSelected), nameof(HasSelection), nameof(OwnerGroup))]
    [NotifyCanExecuteChangedFor(nameof(AddUnitCommand), nameof(AddWarehouseCommand), nameof(DeleteCommand), nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(AddTermCommand))]
    public partial object? SelectedNode { get; set; }

    public SupplyGroup? SelectedGroup => SelectedNode as SupplyGroup;
    public SupplyRow? SelectedRow => SelectedNode as SupplyRow;
    public SupplyGroup? OwnerGroup => SelectedNode switch
    {
        SupplyGroup g => g,
        SupplyRow r => Project.GroupOf(r),
        _ => null,
    };

    public bool IsGroupSelected => SelectedNode is SupplyGroup;
    public bool IsRowSelected => SelectedNode is SupplyRow;
    public bool HasSelection => SelectedNode is not null;

    // ---- Редактор части ----

    public IEnumerable<SupplyRow> GroupWarehouses => SelectedGroup?.Rows.Where(r => r.Kind == RowKind.Warehouse) ?? [];
    public IEnumerable<SupplyRow> GroupUnits => SelectedGroup?.Rows.Where(r => r.Kind == RowKind.Unit) ?? [];

    public SupplyRow? GroupWarehouse
    {
        get => SelectedGroup?.Rows.FirstOrDefault(r => r.Id == SelectedGroup.WarehouseRowId);
        set
        {
            if (SelectedGroup is null)
                return;
            SelectedGroup.WarehouseRowId = value?.Id;
            OnPropertyChanged();
        }
    }

    public SupplyRow? GroupTransport
    {
        get => SelectedGroup?.Rows.FirstOrDefault(r => r.Id == SelectedGroup.TransportRowId);
        set
        {
            if (SelectedGroup is null)
                return;
            SelectedGroup.TransportRowId = value?.Id;
            OnPropertyChanged();
        }
    }

    // ---- Редактор строки ----

    public bool RowIsWarehouse
    {
        get => SelectedRow?.Kind == RowKind.Warehouse;
        set
        {
            if (SelectedRow is null)
                return;
            SelectedRow.Kind = value ? RowKind.Warehouse : RowKind.Unit;
            if (value)
            {
                SelectedRow.LinkProjectId = null;
                SelectedRow.LinkUnitId = null;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedLink));
        }
    }

    public bool RowIsMainWarehouse
    {
        get => SelectedRow is not null && Project.MainWarehouseRowId == SelectedRow.Id;
        set
        {
            if (SelectedRow is null)
                return;
            if (value)
                Project.MainWarehouseRowId = SelectedRow.Id;
            else if (Project.MainWarehouseRowId == SelectedRow.Id)
                Project.MainWarehouseRowId = null;
            OnPropertyChanged();
        }
    }

    public static readonly UnitOption NotLinked = new(Guid.Empty, Guid.Empty, string.Empty, "—", "не привязано");

    public ObservableCollection<UnitOption> LinkOptions { get; } = [];

    public UnitOption? SelectedLink
    {
        get => SelectedRow is { IsLinked: true } row
            ? LinkOptions.FirstOrDefault(o => o.ProjectId == row.LinkProjectId && o.UnitId == row.LinkUnitId)
            : NotLinked;
        set
        {
            if (SelectedRow is null || value is null)
                return;
            if (value == NotLinked)
            {
                SelectedRow.LinkProjectId = null;
                SelectedRow.LinkUnitId = null;
            }
            else
            {
                SelectedRow.LinkProjectId = value.ProjectId;
                SelectedRow.LinkUnitId = value.UnitId;
            }
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveTermCommand))]
    public partial NormTerm? SelectedTerm { get; set; }

    public ObservableCollection<GroupCheck> TermGroups { get; } = [];

    public bool IsSelectedGroupsTerm => SelectedTerm?.Scope == NormScope.SelectedGroups;

    partial void OnSelectedTermChanged(NormTerm? value) => RebuildTermGroups();

    [ObservableProperty]
    public partial string NormFormula { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PreviewDiesel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PreviewPetrol { get; private set; } = string.Empty;

    partial void OnSelectedNodeChanged(object? value)
    {
        // Правки нормы пересчитывают документ, а пересчёт обновляет предпросмотр (Recalculated).
        SelectedTerm = null;
        OnPropertyChanged(nameof(GroupWarehouses));
        OnPropertyChanged(nameof(GroupUnits));
        OnPropertyChanged(nameof(GroupWarehouse));
        OnPropertyChanged(nameof(GroupTransport));
        OnPropertyChanged(nameof(RowIsWarehouse));
        OnPropertyChanged(nameof(RowIsMainWarehouse));
        OnPropertyChanged(nameof(SelectedLink));
        UpdatePreview();
    }

    /// <summary>Обновляет списки привязки (проекты модели 1 могли измениться).</summary>
    public void Refresh()
    {
        LinkOptions.Clear();
        LinkOptions.Add(NotLinked);
        foreach (var option in _session.Model1.UnitOptions())
            LinkOptions.Add(option);
        OnPropertyChanged(nameof(SelectedLink));
        UpdatePreview();
    }

    private void RebuildTermGroups()
    {
        TermGroups.Clear();
        if (SelectedTerm is { Scope: NormScope.SelectedGroups } term)
            foreach (var group in Groups)
                TermGroups.Add(new GroupCheck(group, term));
        OnPropertyChanged(nameof(IsSelectedGroupsTerm));
    }

    public void OnTermScopeChanged()
    {
        RebuildTermGroups();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (SelectedRow is not { } row)
        {
            NormFormula = PreviewDiesel = PreviewPetrol = string.Empty;
            if (SelectedGroup is { } group)
            {
                PreviewDiesel = GroupPreview(group, FuelType.Diesel);
                PreviewPetrol = GroupPreview(group, FuelType.Petrol);
            }
            return;
        }

        NormFormula = row.Norm.Count == 0
            ? "Норма не задана (0 заправок)"
            : "З = " + string.Join(" + ", row.Norm.Select(t => $"{t.Coefficient.ToString("0.##", Ru)} × Мз {ScopeName(t)}"));
        PreviewDiesel = RowPreview(row, FuelType.Diesel);
        PreviewPetrol = RowPreview(row, FuelType.Petrol);
        if (IsSelectedGroupsTerm)
            foreach (var check in TermGroups)
                check.Refresh();
    }

    private string ScopeName(NormTerm term) => term.Scope switch
    {
        NormScope.Own => "своей техники",
        NormScope.Group => "своей части",
        NormScope.Division => "всей рд",
        _ => "частей: " + string.Join(", ", Groups.Where(g => term.GroupIds.Contains(g.Id)).Select(g => g.Name)),
    };

    private string RowPreview(SupplyRow row, FuelType fuel)
    {
        var result = _session.Results(fuel).LastOrDefault()?.Find(row.Id);
        if (result is null)
            return string.Empty;
        var name = fuel.ShortName();
        var own = row.Kind == RowKind.Unit ? $"Мз своей техники {result.OwnRefillMass:N0} кг; " : string.Empty;
        return $"{name}: {own}З = {result.Figures.NormKg:N0} кг ({result.NormRefills.ToString("0.##", Ru)} запр).";
    }

    private string GroupPreview(SupplyGroup group, FuelType fuel)
    {
        var result = _session.Results(fuel).LastOrDefault()?.Groups.FirstOrDefault(g => g.Group == group);
        return result is null ? string.Empty
            : $"{fuel.ShortName()}: Мз части {result.Totals.RefillMass:N0} кг, З части {result.Totals.NormKg:N0} кг.";
    }

    // ---- Команды ----

    [RelayCommand]
    private void AddGroup()
    {
        var name = _dialogs.Prompt("Новая воинская часть",
            "Название части. Для «…рп», «трб» и «управление» строки создаются по типовой структуре:");
        if (name is null)
            return;
        var group = StructureBuilder.ForName(name);
        Groups.Add(group);
        if (Project.MainWarehouseRowId is null && StructureBuilder.Normalize(name).StartsWith("упр", StringComparison.Ordinal))
            Project.MainWarehouseRowId = group.WarehouseRowId;
        SelectedNode = group;
    }

    private bool CanAddRow() => OwnerGroup is not null;

    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private void AddUnit() => AddRow(RowKind.Unit);

    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private void AddWarehouse() => AddRow(RowKind.Warehouse);

    private void AddRow(RowKind kind)
    {
        var group = OwnerGroup!;
        var name = _dialogs.Prompt(kind == RowKind.Unit ? "Новое подразделение" : "Новый склад",
            $"Название в «{group.Name}»:", kind == RowKind.Warehouse ? "Склад " : string.Empty);
        if (name is null)
            return;
        var row = new SupplyRow
        {
            Name = name,
            Kind = kind,
            Norm = kind == RowKind.Unit ? SupplyRow.OwnNorm(1) : [],
        };
        group.Rows.Add(row);
        if (kind == RowKind.Warehouse && group.WarehouseRowId is null)
            group.WarehouseRowId = row.Id;
        SelectedNode = row;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        switch (SelectedNode)
        {
            case SupplyGroup group:
                if (!_dialogs.Confirm($"Удалить воинскую часть «{group.Name}» со всеми строками ({group.Rows.Count})? Данные суток по ним тоже будут удалены."))
                    return;
                foreach (var row in group.Rows.ToList())
                    RemoveRowData(row);
                Groups.Remove(group);
                SelectedNode = null;
                break;
            case SupplyRow row:
                var owner = Project.GroupOf(row)!;
                if (!_dialogs.Confirm($"Удалить «{row.Name}» из «{owner.Name}»? Данные суток по строке тоже будут удалены."))
                    return;
                RemoveRowData(row);
                owner.Rows.Remove(row);
                SelectedNode = owner;
                break;
        }
    }

    private void RemoveRowData(SupplyRow row)
    {
        foreach (var group in Groups)
        {
            if (group.WarehouseRowId == row.Id)
                group.WarehouseRowId = null;
            if (group.TransportRowId == row.Id)
                group.TransportRowId = null;
        }
        if (Project.MainWarehouseRowId == row.Id)
            Project.MainWarehouseRowId = null;
        foreach (var day in Project.Days)
            foreach (var entry in day.Rows.Where(e => e.RowId == row.Id).ToList())
                day.Rows.Remove(entry);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown() => Move(+1);

    private void Move(int direction)
    {
        var node = SelectedNode;
        switch (node)
        {
            case SupplyGroup g:
                Groups.Move(g, direction);
                break;
            case SupplyRow r:
                Project.GroupOf(r)?.Rows.Move(r, direction);
                break;
        }
        SelectedNode = node;
    }

    private bool CanAddTerm() => SelectedRow is not null;

    [RelayCommand(CanExecute = nameof(CanAddTerm))]
    private void AddTerm()
    {
        var term = new NormTerm { Coefficient = 1, Scope = SelectedRow!.Kind == RowKind.Unit && SelectedRow.Norm.Count == 0 ? NormScope.Own : NormScope.Group };
        SelectedRow.Norm.Add(term);
        SelectedTerm = term;
    }

    private bool CanRemoveTerm() => SelectedTerm is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveTerm))]
    private void RemoveTerm()
    {
        SelectedRow?.Norm.Remove(SelectedTerm!);
        SelectedTerm = null;
    }

    [RelayCommand]
    private void Sync()
    {
        _session.RefreshModel1(force: true);
        if (Project.Links.Count == 0)
        {
            _dialogs.Info("Нет привязанных проектов FuelLog. Привяжите их на вкладке «Модели 1».");
            return;
        }
        var result = StructureBuilder.Sync(Project, _session.Model1);
        Refresh();
        _dialogs.Info(result.IsEmpty
            ? "Структура уже соответствует проектам FuelLog — изменений нет."
            : $"Сопоставление выполнено.\n\nСоздано частей: {result.GroupsCreated}\nДобавлено подразделений: {result.RowsCreated}\nПривязано существующих строк: {result.RowsLinked}");
    }
}

public sealed record ScopeOption(NormScope Scope, string Title);

/// <summary>Галочка «часть входит в область нормы» для слагаемого «выбранных частей».</summary>
public sealed partial class GroupCheck(SupplyGroup group, NormTerm term) : ObservableObject
{
    public SupplyGroup Group { get; } = group;

    public bool IsChecked
    {
        get => term.GroupIds.Contains(Group.Id);
        set
        {
            if (value && !term.GroupIds.Contains(Group.Id))
                term.GroupIds.Add(Group.Id);
            else if (!value)
                term.GroupIds.Remove(Group.Id);
            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsChecked));
}
