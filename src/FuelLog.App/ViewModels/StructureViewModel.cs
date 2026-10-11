using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.App.Services;
using FuelLog.Core.Calculation;
using FuelLog.Core.Model;
using FuelLog.Core.Storage;

namespace FuelLog.App.ViewModels;

/// <summary>
/// Вкладка «Структура»: воинские части, подразделения и закреплённая за ними техника.
/// Галочки задают, по каким подразделениям показывается техника справа (одно, несколько или вся в/ч);
/// выделение строки в дереве — над чем выполняются «Переименовать», «Удалить», «↑ ↓».
/// </summary>
public sealed partial class StructureViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly AppSettings? _appSettings;
    private bool _suspendCheckUpdates;

    public StructureViewModel(Project project, IDialogService dialogs, AppSettings? appSettings = null)
    {
        Project = project;
        _dialogs = dialogs;
        _appSettings = appSettings;
        AddCount = 1;
        IsPanelOpen = appSettings?.StructurePanelOpen ?? true;

        project.Groups.CollectionChanged += OnStructureChanged;
        foreach (var group in project.Groups)
            group.Units.CollectionChanged += OnStructureChanged;
        RebuildNodes();
    }

    public Project Project { get; }
    public ObservableCollection<VehicleModel> Catalog => Project.Catalog;

    /// <summary>Дерево для отображения: воинские части с подразделениями и галочками.</summary>
    public ObservableCollection<GroupNode> Nodes { get; } = [];

    // ---- Выделение (для операций над узлом) ----

    /// <summary>Выделенный узел дерева: <see cref="Group"/> или <see cref="Unit"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedGroup), nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(AddUnitCommand), nameof(RenameCommand), nameof(DeleteCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    public partial object? SelectedNode { get; set; }

    public Group? SelectedGroup => SelectedNode switch
    {
        Group g => g,
        Unit u => Project.FindUnit(u.Id)?.Group,
        _ => null,
    };

    public bool HasSelection => SelectedNode is not null;

    // ---- Отмеченные подразделения ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckedCount), nameof(IsNothingChecked), nameof(IsSingleChecked), nameof(IsMultiChecked), nameof(SingleUnit), nameof(AddTargetText), nameof(CanAddToChecked))]
    [NotifyCanExecuteChangedFor(nameof(AddUnitCommand))]
    public partial IReadOnlyList<UnitNode> Checked { get; private set; } = [];

    public int CheckedCount => Checked.Count;
    public bool IsNothingChecked => Checked.Count == 0;
    public bool IsSingleChecked => Checked.Count == 1;
    public bool IsMultiChecked => Checked.Count > 1;
    public UnitNode? SingleUnit => Checked.Count == 1 ? Checked[0] : null;

    [ObservableProperty]
    public partial string HeaderTitle { get; private set; } = string.Empty;

    /// <summary>При нескольких отмеченных: сводка по подразделениям (true) или по маркам (false).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupByModels))]
    public partial bool GroupByUnits { get; set; }

    public bool GroupByModels
    {
        get => !GroupByUnits;
        set => GroupByUnits = !value;
    }

    public ObservableCollection<UnitVehicleRow> UnitRows { get; } = [];
    public ObservableCollection<ModelSummaryRow> ModelRows { get; } = [];
    public ObservableCollection<UnitSummaryRow> UnitSummaryRows { get; } = [];

    [ObservableProperty]
    public partial SummaryFigures? Summary { get; private set; }

    [ObservableProperty]
    public partial string SummaryCaption { get; private set; } = string.Empty;

    // ---- Правая панель ----

    [ObservableProperty]
    public partial bool IsPanelOpen { get; set; }

    partial void OnIsPanelOpenChanged(bool value)
    {
        if (_appSettings is null)
            return;
        _appSettings.StructurePanelOpen = value;
        _appSettings.Save();
    }

    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddVehicleCommand))]
    public partial VehicleModel? AddModel { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddVehicleCommand))]
    public partial int AddCount { get; set; }

    public bool CanAddToChecked => Checked.Count == 1;

    public string AddTargetText => Checked.Count switch
    {
        0 => "Отметьте галочкой одно подразделение, в которое нужно добавить технику.",
        1 => $"В подразделение «{Checked[0].Unit.Name}» ({Checked[0].Group.Name})",
        _ => $"Отмечено подразделений: {Checked.Count}. Для добавления техники оставьте отмеченным одно.",
    };

    // ---- Дерево ----

    private void OnStructureChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Project.Groups))
        {
            foreach (Group g in e.OldItems ?? Array.Empty<Group>())
                g.Units.CollectionChanged -= OnStructureChanged;
            foreach (Group g in e.NewItems ?? Array.Empty<Group>())
                g.Units.CollectionChanged += OnStructureChanged;
        }
        RebuildNodes();
    }

    /// <summary>Перестраивает дерево, сохраняя отметки и выделение.</summary>
    private void RebuildNodes()
    {
        var checkedIds = Nodes.SelectMany(g => g.Units).Where(u => u.IsChecked).Select(u => u.Unit.Id).ToHashSet();

        _suspendCheckUpdates = true;
        Nodes.Clear();
        foreach (var group in Project.Groups)
        {
            var groupNode = new GroupNode(group, this);
            foreach (var unit in group.Units)
                groupNode.Units.Add(new UnitNode(unit, group, groupNode, this) { IsChecked = checkedIds.Contains(unit.Id) });
            Nodes.Add(groupNode);
        }
        _suspendCheckUpdates = false;

        MarkSelected();
        UpdateChecked();
    }

    partial void OnSelectedNodeChanged(object? value)
    {
        MarkSelected();
        // Если ничего не отмечено, щелчок по подразделению сразу показывает его технику.
        if (value is Unit unit && Checked.Count == 0)
            FindNode(unit)?.SetChecked(true);
    }

    private void MarkSelected()
    {
        foreach (var group in Nodes)
        {
            group.IsSelected = ReferenceEquals(group.Group, SelectedNode);
            foreach (var unit in group.Units)
                unit.IsSelected = ReferenceEquals(unit.Unit, SelectedNode);
        }
    }

    internal void OnNodeSelected(object model) => SelectedNode = model;

    internal void OnCheckChanged()
    {
        if (!_suspendCheckUpdates)
            UpdateChecked();
    }

    internal void SetChecked(IEnumerable<UnitNode> units, bool value)
    {
        _suspendCheckUpdates = true;
        foreach (var unit in units)
            unit.IsChecked = value;
        _suspendCheckUpdates = false;
        UpdateChecked();
    }

    private UnitNode? FindNode(Unit unit) => Nodes.SelectMany(g => g.Units).FirstOrDefault(n => n.Unit == unit);

    private void UpdateChecked()
    {
        foreach (var group in Nodes)
            group.NotifyCheckState();
        Checked = Nodes.SelectMany(g => g.Units).Where(u => u.IsChecked).ToList();
        Refresh();
    }

    [RelayCommand]
    private void CheckAll() => SetChecked(Nodes.SelectMany(g => g.Units), true);

    [RelayCommand]
    private void UncheckAll() => SetChecked(Nodes.SelectMany(g => g.Units), false);

    /// <summary>Отметить только это подразделение (двойной щелчок по названию).</summary>
    [RelayCommand]
    private void CheckOnly(UnitNode? node)
    {
        if (node is null)
            return;
        _suspendCheckUpdates = true;
        foreach (var unit in Nodes.SelectMany(g => g.Units))
            unit.IsChecked = unit == node;
        _suspendCheckUpdates = false;
        UpdateChecked();
    }

    // ---- Правая часть ----

    /// <summary>Перестраивает таблицы и сводку (вызывается и при возврате на вкладку).</summary>
    public void Refresh()
    {
        foreach (var row in UnitRows)
            row.PropertyChanged -= OnRowChanged;
        UnitRows.Clear();
        ModelRows.Clear();
        UnitSummaryRows.Clear();

        var units = Checked.Select(n => n.Unit).ToList();

        if (SingleUnit is { } single)
        {
            foreach (var vehicle in single.Unit.Vehicles)
            {
                if (Project.FindModel(vehicle.ModelId) is not { } model)
                    continue;
                var row = new UnitVehicleRow(vehicle, model, Project.Settings);
                row.PropertyChanged += OnRowChanged;
                UnitRows.Add(row);
            }
        }
        else if (Checked.Count > 1)
        {
            var byModel = units
                .SelectMany(u => u.Vehicles)
                .GroupBy(v => v.ModelId)
                .Select(g => (Model: Project.FindModel(g.Key), Count: g.Sum(v => v.Count)))
                .Where(x => x.Model is not null)
                .OrderBy(x => x.Model!.Name, StringComparer.CurrentCulture);
            foreach (var (model, count) in byModel)
                ModelRows.Add(new ModelSummaryRow(model!, count, count * model!.TankLiters * Project.Settings.DensityOf(model.Fuel)));

            foreach (var node in Checked)
                UnitSummaryRows.Add(new UnitSummaryRow(node.Unit, node.Group, SummaryFigures.Of(Project, [node.Unit])));
        }

        HeaderTitle = BuildHeader();
        UpdateSummary();
    }

    private string BuildHeader()
    {
        if (Checked.Count == 0)
            return "Подразделения не отмечены";
        if (SingleUnit is { } single)
            return $"{single.Group.Name} — {single.Unit.Name}";

        var groups = Checked.Select(n => n.Group).Distinct().ToList();
        if (groups.Count == 1 && groups[0].Units.Count == Checked.Count)
            return $"{groups[0].Name} — все подразделения ({Checked.Count})";
        var allUnits = Nodes.SelectMany(g => g.Units).Count();
        if (Checked.Count == allUnits)
            return $"Все подразделения ({allUnits})";
        return $"Отмечено подразделений: {Checked.Count}";
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => UpdateSummary();

    private void UpdateSummary()
    {
        if (Checked.Count == 0)
        {
            Summary = SummaryFigures.Of(Project, Project.AllUnits);
            SummaryCaption = "Весь проект";
        }
        else
        {
            Summary = SummaryFigures.Of(Project, Checked.Select(n => n.Unit));
            SummaryCaption = Checked.Count == 1 ? "Отмеченное подразделение" : $"Отмечено подразделений: {Checked.Count}";
        }
    }

    // ---- Команды структуры ----

    [RelayCommand]
    private void AddGroup()
    {
        var name = _dialogs.Prompt("Новая воинская часть", "Название воинской части (рп):");
        if (name is null)
            return;
        var group = new Group { Name = name };
        Project.Groups.Add(group);
        SelectedNode = group;
    }

    /// <summary>Куда добавлять подразделение: выделенная в/ч, а если ничего не выделено — в/ч отмеченных подразделений.</summary>
    private Group? UnitTargetGroup => SelectedGroup
        ?? (Checked.Select(n => n.Group).Distinct().ToList() is [var only] ? only : null);

    private bool CanAddUnit() => UnitTargetGroup is not null;

    [RelayCommand(CanExecute = nameof(CanAddUnit))]
    private void AddUnit()
    {
        var group = UnitTargetGroup!;
        var name = _dialogs.Prompt("Новое подразделение", $"Название подразделения в «{group.Name}»:");
        if (name is null)
            return;
        var unit = new Unit { Name = name };
        group.Units.Add(unit);
        SelectedNode = unit;
        // Новое подразделение сразу готово к добавлению техники.
        CheckOnly(FindNode(unit));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Rename()
    {
        switch (SelectedNode)
        {
            case Group g when _dialogs.Prompt("Переименование", "Новое название воинской части:", g.Name) is { } name:
                g.Name = name;
                break;
            case Unit u when _dialogs.Prompt("Переименование", "Новое название подразделения:", u.Name) is { } name:
                u.Name = name;
                break;
        }
        HeaderTitle = BuildHeader();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        switch (SelectedNode)
        {
            case Group g:
            {
                var items = g.Units.Sum(Project.TaskItemsOf);
                var message = $"Удалить воинскую часть «{g.Name}» и все её подразделения ({g.Units.Count})?";
                if (items > 0)
                    message += $"\n\nТехника этих подразделений задействована в задачах — будет удалено строк задач: {items}.";
                if (_dialogs.Confirm(message))
                {
                    Project.RemoveGroup(g);
                    SelectedNode = null;
                }
                break;
            }
            case Unit u:
            {
                var group = SelectedGroup!;
                var items = Project.TaskItemsOf(u);
                var message = $"Удалить подразделение «{u.Name}» вместе с закреплённой техникой?";
                if (items > 0)
                    message += $"\n\nТехника подразделения задействована в задачах — будет удалено строк задач: {items}.";
                if (_dialogs.Confirm(message))
                {
                    Project.RemoveUnit(group, u);
                    SelectedNode = group;
                }
                break;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp() => MoveSelected(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown() => MoveSelected(+1);

    private void MoveSelected(int direction)
    {
        switch (SelectedNode)
        {
            case Group g:
                Project.Groups.Move(g, direction);
                break;
            case Unit u:
                SelectedGroup?.Units.Move(u, direction);
                break;
        }
    }

    private bool CanAddVehicle() => AddModel is not null && AddCount > 0;

    [RelayCommand(CanExecute = nameof(CanAddVehicle))]
    private void AddVehicle()
    {
        if (SingleUnit is not { } target)
        {
            _dialogs.Warn(Checked.Count == 0
                ? "Чтобы добавить технику, отметьте галочкой одно подразделение."
                : $"Добавлять технику можно только в одно подразделение, а отмечено {Checked.Count}.\n\n" +
                  "Оставьте отмеченным одно подразделение (двойной щелчок по названию отмечает только его).");
            return;
        }

        var unit = target.Unit;
        var existing = unit.Vehicles.FirstOrDefault(v => v.ModelId == AddModel!.Id);
        if (existing is not null)
            existing.Count += AddCount;
        else
            unit.Vehicles.Add(new UnitVehicle { ModelId = AddModel!.Id, Count = AddCount });

        AddModel = null;
        AddCount = 1;
        Refresh();
    }

    [RelayCommand]
    private void RemoveVehicle(UnitVehicleRow? row)
    {
        if (row is null || SingleUnit?.Unit is not { } unit)
            return;

        var used = Project.Tasks.SelectMany(t => t.Items).Count(i => i.UnitId == unit.Id && i.ModelId == row.Model.Id);
        var message = $"Убрать «{row.Model.Name}» ({row.Count} ед.) из подразделения «{unit.Name}»?";
        if (used > 0)
            message += $"\n\nЭта техника задействована в задачах (строк: {used}). Строки задач останутся, но в отчёте появится предупреждение о нехватке техники.";
        if (!_dialogs.Confirm(message))
            return;

        unit.Vehicles.Remove(row.Vehicle);
        Refresh();
    }
}

/// <summary>Узел дерева «воинская часть». Галочка отражает отметки подразделений (все / часть / ни одного).</summary>
public sealed partial class GroupNode(Group group, StructureViewModel owner) : ObservableObject
{
    public Group Group { get; } = group;
    public ObservableCollection<UnitNode> Units { get; } = [];

    public bool? IsChecked
    {
        get
        {
            if (Units.Count == 0)
                return false;
            var count = Units.Count(u => u.IsChecked);
            return count == 0 ? false : count == Units.Count ? true : null;
        }
        set => owner.SetChecked(Units, value == true);
    }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            owner.OnNodeSelected(Group);
    }

    internal void NotifyCheckState() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>Узел дерева «подразделение».</summary>
public sealed partial class UnitNode(Unit unit, Group group, GroupNode parent, StructureViewModel owner) : ObservableObject
{
    public Unit Unit { get; } = unit;
    public Group Group { get; } = group;
    public GroupNode Parent { get; } = parent;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsCheckedChanged(bool value) => owner.OnCheckChanged();

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            owner.OnNodeSelected(Unit);
    }

    internal void SetChecked(bool value) => IsChecked = value;
}

/// <summary>Строка состава подразделения (редактируется количество).</summary>
public sealed partial class UnitVehicleRow : ObservableObject
{
    private readonly ProjectSettings _settings;

    public UnitVehicleRow(UnitVehicle vehicle, VehicleModel model, ProjectSettings settings)
    {
        Vehicle = vehicle;
        Model = model;
        _settings = settings;
    }

    public UnitVehicle Vehicle { get; }
    public VehicleModel Model { get; }

    public int Count
    {
        get => Vehicle.Count;
        set
        {
            if (value < 0 || value == Vehicle.Count)
                return;
            Vehicle.Count = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalLiters));
            OnPropertyChanged(nameof(TotalKg));
        }
    }

    public double TotalLiters => Count * Model.TankLiters;
    public double TotalKg => TotalLiters * _settings.DensityOf(Model.Fuel);
    public string TanksText => Tanks.Text(Model);
}

internal static class Tanks
{
    /// <summary>«250 + 125» — основной и дополнительный бак, или один объём, если доп. бака нет.</summary>
    public static string Text(VehicleModel model) => model.ExtraTankLiters > 0
        ? $"{model.MainTankLiters:N0} + {model.ExtraTankLiters:N0}"
        : $"{model.MainTankLiters:N0}";
}

/// <summary>Марка в сводке по нескольким подразделениям.</summary>
public sealed record ModelSummaryRow(VehicleModel Model, int Count, double TotalKg)
{
    public double TotalLiters => Count * Model.TankLiters;
    public string TanksText => Tanks.Text(Model);
}

public sealed record UnitSummaryRow(Unit Unit, Group Group, SummaryFigures Figures);

/// <summary>Сводка по набору подразделений: количество техники, объём и масса полной заправки.</summary>
public sealed record SummaryFigures(int Units, int Vehicles, int PetrolVehicles, int DieselVehicles, FuelPair Liters, FuelPair Kg)
{
    public static SummaryFigures Of(Project project, IEnumerable<Unit> units)
    {
        var list = units.ToList();
        int petrol = 0, diesel = 0;
        foreach (var vehicle in list.SelectMany(u => u.Vehicles))
        {
            var model = project.FindModel(vehicle.ModelId);
            if (model?.Fuel == FuelType.Petrol)
                petrol += vehicle.Count;
            else if (model?.Fuel == FuelType.Diesel)
                diesel += vehicle.Count;
        }

        return new SummaryFigures(
            list.Count,
            petrol + diesel,
            petrol,
            diesel,
            FuelPair.Sum(list.Select(u => FuelCalculator.RefillVolume(u, project))),
            FuelPair.Sum(list.Select(u => FuelCalculator.RefillMass(u, project))));
    }
}
