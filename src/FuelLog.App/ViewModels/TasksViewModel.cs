using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.App.Services;
using FuelLog.Core.Calculation;
using FuelLog.Core.Model;

namespace FuelLog.App.ViewModels;

/// <summary>Вкладка «Задачи»: список задач по датам и состав техники выбранной задачи.</summary>
public sealed partial class TasksViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;

    public TasksViewModel(Project project, IDialogService dialogs)
    {
        Project = project;
        _dialogs = dialogs;

        var view = new ListCollectionView(project.Tasks) { IsLiveSorting = true };
        view.SortDescriptions.Add(new SortDescription(nameof(FuelTask.Date), ListSortDirection.Ascending));
        view.LiveSortingProperties.Add(nameof(FuelTask.Date));
        TasksView = view;

        project.Tasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TasksSummary));
        SelectedTask = project.Tasks.OrderBy(t => t.Date).LastOrDefault();
    }

    public Project Project { get; }
    public ICollectionView TasksView { get; }
    public ObservableCollection<string> TaskTypes => Project.Settings.TaskTypes;

    public string TasksSummary
    {
        get
        {
            var included = Project.Tasks.Count(t => t.IsIncluded);
            return $"Задач: {Project.Tasks.Count}, в расчёте: {included}";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTask))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateTaskCommand), nameof(DeleteTaskCommand), nameof(AddPickedCommand), nameof(AddWholeUnitCommand), nameof(ApplyDefaultsToAllCommand))]
    public partial FuelTask? SelectedTask { get; set; }

    public bool HasTask => SelectedTask is not null;

    public ObservableCollection<TaskItemRow> ItemRows { get; } = [];

    [ObservableProperty]
    public partial TaskTotals Totals { get; private set; } = TaskTotals.Empty;

    // ---- Панель добавления техники ----

    public ObservableCollection<UnitChoice> UnitChoices { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddPickedCommand), nameof(AddWholeUnitCommand))]
    public partial UnitChoice? PickUnit { get; set; }

    public ObservableCollection<PickRow> PickRows { get; } = [];

    [ObservableProperty]
    public partial double DefaultDistanceKm { get; set; }

    [ObservableProperty]
    public partial double DefaultWorkHours { get; set; }

    [ObservableProperty]
    public partial double DefaultLossPercent { get; set; }

    private string? _previousTaskType;

    partial void OnSelectedTaskChanging(FuelTask? value)
    {
        if (SelectedTask is not null)
        {
            SelectedTask.PropertyChanging -= OnTaskPropertyChanging;
            SelectedTask.PropertyChanged -= OnTaskPropertyChanged;
            SelectedTask.Items.CollectionChanged -= OnItemsCollectionChanged;
        }
    }

    partial void OnSelectedTaskChanged(FuelTask? value)
    {
        if (value is not null)
        {
            value.PropertyChanging += OnTaskPropertyChanging;
            value.PropertyChanged += OnTaskPropertyChanged;
            value.Items.CollectionChanged += OnItemsCollectionChanged;
        }
        RebuildRows();
    }

    private void OnTaskPropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (e.PropertyName == nameof(FuelTask.TaskType))
            _previousTaskType = SelectedTask?.TaskType;
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Название по умолчанию совпадает с типом задачи — пока пользователь не ввёл своё.
        if (e.PropertyName == nameof(FuelTask.TaskType) && SelectedTask is { } task
            && (string.IsNullOrWhiteSpace(task.Name) || task.Name == _previousTaskType))
            task.Name = task.TaskType;
        if (e.PropertyName == nameof(FuelTask.IsIncluded))
            OnPropertyChanged(nameof(TasksSummary));
    }

    private void OnItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RebuildRows();

    /// <summary>Обновляет данные, зависящие от других вкладок (справочник, структура, плотности).</summary>
    public void Refresh()
    {
        UnitChoices.Clear();
        foreach (var group in Project.Groups)
            foreach (var unit in group.Units)
                UnitChoices.Add(new UnitChoice(group, unit));

        if (PickUnit is not null)
            PickUnit = UnitChoices.FirstOrDefault(c => c.Unit == PickUnit.Unit);
        else
            RebuildPickRows();

        RebuildRows();
        OnPropertyChanged(nameof(TasksSummary));
    }

    partial void OnPickUnitChanged(UnitChoice? value) => RebuildPickRows();

    private void RebuildPickRows()
    {
        PickRows.Clear();
        if (PickUnit is null)
            return;
        foreach (var vehicle in PickUnit.Unit.Vehicles)
        {
            if (Project.FindModel(vehicle.ModelId) is { } model)
                PickRows.Add(new PickRow(model, vehicle.Count));
        }
    }

    private void RebuildRows()
    {
        foreach (var row in ItemRows)
        {
            row.PropertyChanged -= OnRowChanged;
            row.Detach();
        }
        ItemRows.Clear();

        if (SelectedTask is not null)
        {
            foreach (var item in SelectedTask.Items)
            {
                var location = Project.FindUnit(item.UnitId);
                var model = Project.FindModel(item.ModelId);
                var row = new TaskItemRow(item, location?.Group, location?.Unit, model, Project.Settings);
                row.PropertyChanged += OnRowChanged;
                ItemRows.Add(row);
            }
        }
        UpdateTotals();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => UpdateTotals();

    private void UpdateTotals() => Totals = TaskTotals.Of(ItemRows);

    [RelayCommand]
    private void AddTask()
    {
        var type = TaskTypes.FirstOrDefault() ?? "Задача";
        var task = new FuelTask
        {
            TaskType = type,
            Name = type,
            Date = SelectedTask?.Date ?? DateOnly.FromDateTime(DateTime.Today),
        };
        Project.Tasks.Add(task);
        SelectedTask = task;
    }

    [RelayCommand(CanExecute = nameof(HasTask))]
    private void DuplicateTask()
    {
        var copy = SelectedTask!.Clone();
        Project.Tasks.Insert(Project.Tasks.IndexOf(SelectedTask) + 1, copy);
        SelectedTask = copy;
    }

    [RelayCommand(CanExecute = nameof(HasTask))]
    private void DeleteTask()
    {
        var task = SelectedTask!;
        if (!_dialogs.Confirm($"Удалить задачу «{task.Name}» от {task.Date:dd.MM.yyyy}?"))
            return;
        var ordered = Project.Tasks.OrderBy(t => t.Date).ToList();
        var index = ordered.IndexOf(task);
        Project.Tasks.Remove(task);
        ordered.Remove(task);
        SelectedTask = ordered.Count == 0 ? null : ordered[Math.Min(index, ordered.Count - 1)];
    }

    [RelayCommand]
    private void IncludeAll() => SetIncluded(true);

    [RelayCommand]
    private void ExcludeAll() => SetIncluded(false);

    private void SetIncluded(bool value)
    {
        foreach (var task in Project.Tasks)
            task.IsIncluded = value;
    }

    private bool CanAddPicked() => SelectedTask is not null && PickUnit is not null;

    [RelayCommand(CanExecute = nameof(CanAddPicked))]
    private void AddPicked()
    {
        var picked = PickRows.Where(r => r.Count > 0).ToList();
        if (picked.Count == 0)
        {
            _dialogs.Info("Укажите количество техники в колонке «Добавить».");
            return;
        }
        AddRows(picked.Select(r => (r.Model, r.Count)));
        foreach (var row in picked)
            row.Count = 0;
    }

    [RelayCommand(CanExecute = nameof(CanAddPicked))]
    private void AddWholeUnit() => AddRows(PickRows.Where(r => r.Available > 0).Select(r => (r.Model, r.Available)));

    private void AddRows(IEnumerable<(VehicleModel Model, int Count)> rows)
    {
        foreach (var (model, count) in rows)
        {
            SelectedTask!.Items.Add(new TaskItem
            {
                UnitId = PickUnit!.Unit.Id,
                ModelId = model.Id,
                Count = count,
                DistanceKm = DefaultDistanceKm,
                WorkHours = DefaultWorkHours,
                LossPercent = DefaultLossPercent,
            });
        }
    }

    [RelayCommand]
    private void RemoveItem(TaskItemRow? row)
    {
        if (row is not null)
            SelectedTask?.Items.Remove(row.Item);
    }

    [RelayCommand(CanExecute = nameof(HasTask))]
    private void ApplyDefaultsToAll()
    {
        if (ItemRows.Count == 0)
            return;
        if (!_dialogs.Confirm($"Установить всем строкам задачи: марш {DefaultDistanceKm:0.##} км, работа {DefaultWorkHours:0.##} ч, потери {DefaultLossPercent:0.##} %?"))
            return;
        foreach (var row in ItemRows)
        {
            row.DistanceKm = DefaultDistanceKm;
            row.WorkHours = DefaultWorkHours;
            row.LossPercent = DefaultLossPercent;
        }
    }
}

public sealed record UnitChoice(Group Group, Unit Unit)
{
    public string Display => $"{Group.Name} — {Unit.Name}";
}

/// <summary>Техника выбранного подразделения в панели добавления.</summary>
public sealed partial class PickRow(VehicleModel model, int available) : ObservableObject
{
    public VehicleModel Model { get; } = model;
    public int Available { get; } = available;

    [ObservableProperty]
    public partial int Count { get; set; }

    partial void OnCountChanged(int value)
    {
        if (value < 0)
            Count = 0;
        else if (value > Available)
            Count = Available;
    }
}

/// <summary>Строка состава задачи с расчётом расхода «на лету».</summary>
public sealed partial class TaskItemRow : ObservableObject
{
    private readonly ProjectSettings _settings;

    public TaskItemRow(TaskItem item, Group? group, Unit? unit, VehicleModel? model, ProjectSettings settings)
    {
        Item = item;
        Group = group;
        Unit = unit;
        Model = model;
        _settings = settings;
        item.PropertyChanged += OnItemChanged;
        Recalculate();
    }

    public void Detach() => Item.PropertyChanged -= OnItemChanged;

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Recalculate();

    public TaskItem Item { get; }
    public Group? Group { get; }
    public Unit? Unit { get; }
    public VehicleModel? Model { get; }

    public bool IsBroken => Unit is null || Model is null;
    public string UnitName => Unit is null ? "(удалено)" : $"{Group?.Name} — {Unit.Name}";
    public string ModelName => Model?.Name ?? "(удалена)";
    public string FuelName => Model?.FuelName ?? string.Empty;

    public int Available => Unit is null || Model is null
        ? 0
        : Unit.Vehicles.Where(v => v.ModelId == Model.Id).Sum(v => v.Count);

    public bool IsOverAvailable => Item.Count > Available;

    public int Count
    {
        get => Item.Count;
        set => Item.Count = Math.Max(0, value);
    }

    public double DistanceKm
    {
        get => Item.DistanceKm;
        set => Item.DistanceKm = Math.Max(0, value);
    }

    public double WorkHours
    {
        get => Item.WorkHours;
        set => Item.WorkHours = Math.Max(0, value);
    }

    public double LossPercent
    {
        get => Item.LossPercent;
        set => Item.LossPercent = Math.Clamp(value, 0, 100);
    }

    [ObservableProperty]
    public partial ItemCalculation? Calc { get; private set; }

    public double Liters => Calc?.TotalLiters ?? 0;
    public double Kg => Calc?.TotalKg ?? 0;
    public int LostCount => Calc?.LostCount ?? 0;

    private void Recalculate()
    {
        Calc = Model is null ? null : FuelCalculator.Calculate(Item, Model, _settings);
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(DistanceKm));
        OnPropertyChanged(nameof(WorkHours));
        OnPropertyChanged(nameof(LossPercent));
        OnPropertyChanged(nameof(Liters));
        OnPropertyChanged(nameof(Kg));
        OnPropertyChanged(nameof(LostCount));
        OnPropertyChanged(nameof(IsOverAvailable));
    }
}

public sealed record TaskTotals(int Vehicles, int Lost, double PetrolKg, double DieselKg, int Problems)
{
    public static readonly TaskTotals Empty = new(0, 0, 0, 0, 0);

    public static TaskTotals Of(IEnumerable<TaskItemRow> rows)
    {
        var list = rows.ToList();
        return new TaskTotals(
            list.Sum(r => r.Count),
            list.Sum(r => r.LostCount),
            list.Where(r => r.Calc?.Fuel == FuelType.Petrol).Sum(r => r.Kg),
            list.Where(r => r.Calc?.Fuel == FuelType.Diesel).Sum(r => r.Kg),
            list.Count(r => r.IsBroken || r.IsOverAvailable));
    }
}
