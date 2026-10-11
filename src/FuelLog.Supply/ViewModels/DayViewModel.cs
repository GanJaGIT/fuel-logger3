using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.Core.Model;
using FuelLog.Supply.Core.Calculation;
using FuelLog.Supply.Core.Model;
using FuelLog.Supply.Core.Reporting;
using FuelLog.Supply.Services;

namespace FuelLog.Supply.ViewModels;

/// <summary>Экран «Сутки»: документ «Потребность и автономность» и операции выбранных суток.</summary>
public sealed partial class DayViewModel : ObservableObject, IDisposable
{
    private readonly SupplySession _session;
    private readonly IDialogService _dialogs;
    private string _structureKey = string.Empty;

    public DayViewModel(SupplySession session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        _session.Recalculated += OnRecalculated;
        Fuel = FuelType.Diesel;
        SelectedDay = Project.Days.LastOrDefault();
    }

    public SupplyProject Project => _session.Project;
    public ObservableCollection<SupplyDay> Days => Project.Days;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDay), nameof(DayTitle), nameof(DayPosition))]
    [NotifyCanExecuteChangedFor(nameof(PreviousDayCommand), nameof(NextDayCommand), nameof(RemoveDayCommand), nameof(ExportCommand))]
    public partial SupplyDay? SelectedDay { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiesel), nameof(IsPetrol), nameof(FuelTitle))]
    public partial FuelType Fuel { get; set; }

    public bool IsDiesel
    {
        get => Fuel == FuelType.Diesel;
        set
        {
            if (value)
                Fuel = FuelType.Diesel;
        }
    }

    public bool IsPetrol
    {
        get => Fuel == FuelType.Petrol;
        set
        {
            if (value)
                Fuel = FuelType.Petrol;
        }
    }

    public string FuelTitle => Fuel == FuelType.Diesel ? "ДТ" : "АБ";
    public bool HasDay => SelectedDay is not null;

    public string DayTitle => SelectedDay is null
        ? "Суток в документе нет"
        : SelectedDay.Date.ToString("d MMMM yyyy 'г.', dddd", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));

    public string DayPosition => SelectedDay is null ? string.Empty : $"Сутки {Days.IndexOf(SelectedDay) + 1} из {Days.Count}";

    public ObservableCollection<DocRow> DocumentRows { get; } = [];
    public ObservableCollection<RefuelRow> RefuelRows { get; } = [];
    public ObservableCollection<DeliveryRow> DeliveryRows { get; } = [];
    public ObservableCollection<TransportRow> TransportRows { get; } = [];
    public ObservableCollection<LossRow> LossRows { get; } = [];
    public ObservableCollection<TaskRow> TaskRows { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty]
    public partial Figures? Total { get; private set; }

    [ObservableProperty]
    public partial string TodayOffer { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Model1Dates { get; private set; } = string.Empty;

    partial void OnSelectedDayChanged(SupplyDay? value) => Rebuild();

    partial void OnFuelChanged(FuelType value) => Rebuild();

    private void OnRecalculated(object? sender, EventArgs e)
    {
        if (StructureKey() != _structureKey || (SelectedDay is not null && !Days.Contains(SelectedDay)))
            Rebuild();
        else
            Update();
        OnPropertyChanged(nameof(DayPosition));
    }

    /// <summary>Отпечаток всего, что влияет на состав строк: при его смене таблицы строятся заново.</summary>
    private string StructureKey() =>
        string.Join("|", Project.Groups.Select(g =>
            $"{g.Id}{g.Name}:{string.Join(",", g.Rows.Select(r => $"{r.Id}{r.Name}{r.Kind}{r.LinkUnitId}"))}:{g.WarehouseRowId}{g.TransportRowId}:" +
            string.Join(",", g.Transport.Select(t => $"{t.ModelId}x{t.Count}"))))
        + string.Join(",", Project.TransportCatalog.Select(t => $"{t.Id}{t.CapacityLiters}{t.Fuel}"))
        + Project.Settings.DieselDensity + Project.Settings.PetrolDensity
        + Project.MainWarehouseRowId + SelectedDay?.GetHashCode() + Fuel;

    /// <summary>Создаёт строки всех таблиц заново (смена суток, топлива или структуры).</summary>
    private void Rebuild()
    {
        if (SelectedDay is not null && !Days.Contains(SelectedDay))
        {
            SelectedDay = Days.LastOrDefault();
            return;
        }

        DocumentRows.Clear();
        RefuelRows.Clear();
        DeliveryRows.Clear();
        TransportRows.Clear();
        LossRows.Clear();
        TaskRows.Clear();
        _structureKey = StructureKey();

        if (SelectedDay is { } day)
        {
            var main = Project.MainWarehouseRowId;
            foreach (var group in Project.Groups)
            {
                DocumentRows.Add(DocRow.Header(group.Name));
                foreach (var row in group.Rows)
                {
                    DocumentRows.Add(DocRow.ForRow(group, row));
                    LossRows.Add(new LossRow(day, group, row, Fuel));
                    if (row.Kind == RowKind.Unit)
                    {
                        RefuelRows.Add(new RefuelRow(day, group, row, Fuel, SupplierName(group, row)));
                        TaskRows.Add(new TaskRow(group, row, LinkName(row)));
                    }
                    else
                    {
                        DeliveryRows.Add(new DeliveryRow(day, group, row, Fuel, row.Id == main));
                    }
                }
                DocumentRows.Add(DocRow.GroupTotal(group));
                if (group.TransportRowId is not null || group.Transport.Count > 0)
                    TransportRows.Add(new TransportRow(day, Project, group, Fuel));
            }
            if (Project.Groups.Count > 1)
                DocumentRows.Add(DocRow.GrandTotal());
        }
        Update();
    }

    /// <summary>Обновляет рассчитанные показатели в существующих строках.</summary>
    private void Update()
    {
        var result = _session.Result(SelectedDay, Fuel);
        Total = result?.Total;

        Warnings.Clear();
        foreach (var warning in result?.Warnings ?? [])
            Warnings.Add(warning);

        if (result is not null)
        {
            foreach (var doc in DocumentRows)
                doc.Apply(result);
            foreach (var r in RefuelRows)
                r.Apply(result.Find(r.Row.Id));
            foreach (var r in DeliveryRows)
                r.Apply(result.Find(r.Row.Id));
            foreach (var r in TransportRows)
                r.Apply(result);
            foreach (var r in TaskRows)
                r.Apply(result.Find(r.Row.Id), _session.Model1.DailyAverage(r.Row)[Fuel]);
        }

        var dates = _session.Model1.TaskDates();
        Model1Dates = dates.Count == 0
            ? "В привязанных проектах FuelLog нет задач, отмеченных «в расчёт»."
            : $"Задачи в проектах FuelLog: {string.Join(", ", dates.Select(d => d.ToString("dd.MM.yyyy")))}";
    }

    private string SupplierName(SupplyGroup group, SupplyRow row)
    {
        var supplier = Project.FindRow(group.WarehouseRowId) ?? Project.FindRow(Project.MainWarehouseRowId);
        return supplier is { } s && s.Row.Id != row.Id ? $"{s.Row.Name} ({s.Group.Name})" : "не назначен";
    }

    private string LinkName(SupplyRow row) =>
        _session.Model1.FindUnit(row) is { } found ? $"{found.Group.Name} — {found.Unit.Name}" : row.IsLinked ? "недоступно" : "не привязано";

    // ---- Сутки ----

    /// <summary>Если документ не доведён до сегодняшнего дня — предлагаем создать сутки.</summary>
    public void OfferToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var last = Days.Count == 0 ? (DateOnly?)null : Days.Max(d => d.Date);
        TodayOffer = last is null
            ? "В документе ещё нет суток."
            : last < today
                ? $"Последние сутки в документе — {last:dd.MM.yyyy}, сегодня {today:dd.MM.yyyy}."
                : string.Empty;
    }

    private bool CanGoPrevious() => SelectedDay is not null && Days.IndexOf(SelectedDay) > 0;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousDay() => SelectedDay = Days[Days.IndexOf(SelectedDay!) - 1];

    private bool CanGoNext() => SelectedDay is not null && Days.IndexOf(SelectedDay) < Days.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextDay() => SelectedDay = Days[Days.IndexOf(SelectedDay!) + 1];

    /// <summary>Следующие сутки: остаток = наличие последних суток, состояние транспорта переносится.</summary>
    [RelayCommand]
    private void AddDay()
    {
        var date = DayOperations.NextDate(Project, DateOnly.FromDateTime(DateTime.Today));
        SelectedDay = DayOperations.AddDay(Project, date);
        OfferToday();
    }

    [RelayCommand]
    private void AddToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (Days.FirstOrDefault(d => d.Date == today) is { } existing)
        {
            SelectedDay = existing;
        }
        else
        {
            if (Days.Count > 0 && Days.Max(d => d.Date) > today)
            {
                _dialogs.Warn("В документе уже есть сутки позже сегодняшней даты.");
                return;
            }
            var missing = Days.Count == 0 ? 0 : today.DayNumber - Days.Max(d => d.Date).DayNumber - 1;
            if (missing > 0 && !_dialogs.Confirm(
                    $"Между последними сутками и сегодняшним днём пропущено суток: {missing}.\n\n" +
                    "Остаток на сегодня будет взят из наличия последних суток в документе. Продолжить?"))
                return;
            SelectedDay = DayOperations.AddDay(Project, today);
        }
        OfferToday();
    }

    private bool CanRemove() => SelectedDay is not null && DayOperations.CanRemove(Project, SelectedDay);

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void RemoveDay()
    {
        var day = SelectedDay!;
        if (!_dialogs.Confirm($"Удалить сутки {day.Date:dd.MM.yyyy} со всеми операциями и потерями?"))
            return;
        Days.Remove(day);
        SelectedDay = Days.LastOrDefault();
        OfferToday();
    }

    // ---- Операции ----

    [RelayCommand]
    private void RefuelAll() => SetRefuel(true);

    [RelayCommand]
    private void RefuelNone() => SetRefuel(false);

    private void SetRefuel(bool value)
    {
        foreach (var row in RefuelRows)
            row.IsChecked = value;
    }

    [RelayCommand]
    private void RefreshModel1() => _session.RefreshModel1(force: true);

    [RelayCommand(CanExecute = nameof(HasDay))]
    private void Export()
    {
        var day = SelectedDay!;
        var diesel = _session.Result(day, FuelType.Diesel);
        var petrol = _session.Result(day, FuelType.Petrol);
        if (diesel is null || petrol is null)
            return;
        try
        {
            Directory.CreateDirectory(_session.ReportsFolder);
            var path = Path.Combine(_session.ReportsFolder, $"Потребность и автономность {day.Date:yyyy-MM-dd} ({DateTime.Now:HH-mm-ss}).xlsx");
            SupplyExcelWriter.Write(Project, diesel, petrol, path);
            _dialogs.OpenWithShell(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Не удалось сохранить документ: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenReportsFolder()
    {
        Directory.CreateDirectory(_session.ReportsFolder);
        _dialogs.ShowInExplorer(_session.ReportsFolder);
    }

    public void Dispose() => _session.Recalculated -= OnRecalculated;
}

public enum DocRowKind
{
    Header,
    Row,
    GroupTotal,
    GrandTotal,
}

/// <summary>Строка документа «Потребность и автономность» (17 граф).</summary>
public sealed partial class DocRow : ObservableObject
{
    private DocRow(DocRowKind kind, string name, SupplyGroup? group, SupplyRow? row)
    {
        Kind = kind;
        Name = name;
        Group = group;
        Row = row;
    }

    public DocRowKind Kind { get; }
    public string Name { get; }
    public SupplyGroup? Group { get; }
    public SupplyRow? Row { get; }

    public bool IsHeader => Kind == DocRowKind.Header;
    public bool IsTotal => Kind is DocRowKind.GroupTotal or DocRowKind.GrandTotal;
    public bool IsWarehouse => Row?.Kind == RowKind.Warehouse;

    public static DocRow Header(string name) => new(DocRowKind.Header, name, null, null);
    public static DocRow ForRow(SupplyGroup group, SupplyRow row) => new(DocRowKind.Row, row.Name, group, row);
    public static DocRow GroupTotal(SupplyGroup group) => new(DocRowKind.GroupTotal, "Итого", group, null);
    public static DocRow GrandTotal() => new(DocRowKind.GrandTotal, "Итого по рд", null, null);

    [ObservableProperty] public partial double? NormKg { get; private set; }
    [ObservableProperty] public partial double? NormRefills { get; private set; }
    [ObservableProperty] public partial double? OpeningKg { get; private set; }
    [ObservableProperty] public partial double? OpeningRefills { get; private set; }
    [ObservableProperty] public partial double? IncomeKg { get; private set; }
    [ObservableProperty] public partial double? IncomeRefills { get; private set; }
    [ObservableProperty] public partial double? ExpenseKg { get; private set; }
    [ObservableProperty] public partial double? ExpenseRefills { get; private set; }
    [ObservableProperty] public partial double? LossKg { get; private set; }
    [ObservableProperty] public partial double? LossRefills { get; private set; }
    [ObservableProperty] public partial double? BalanceKg { get; private set; }
    [ObservableProperty] public partial double? BalanceRefills { get; private set; }
    [ObservableProperty] public partial double? NeedKg { get; private set; }
    [ObservableProperty] public partial double? NeedRefills { get; private set; }
    [ObservableProperty] public partial double? Autonomy { get; private set; }
    [ObservableProperty] public partial bool IsShortage { get; private set; }
    [ObservableProperty] public partial bool IsNegative { get; private set; }

    public void Apply(DayResult result)
    {
        Figures? f;
        double? normRefills = null;
        switch (Kind)
        {
            case DocRowKind.Row:
                var line = result.Find(Row!.Id);
                f = line?.Figures;
                normRefills = line?.NormRefills;
                break;
            case DocRowKind.GroupTotal:
                f = result.Groups.FirstOrDefault(g => g.Group == Group)?.Totals;
                break;
            case DocRowKind.GrandTotal:
                f = result.Total;
                break;
            default:
                return;
        }
        if (f is null)
            return;

        NormKg = f.NormKg;
        NormRefills = normRefills;
        OpeningKg = f.OpeningKg;
        OpeningRefills = f.Refills(f.OpeningKg);
        IncomeKg = f.IncomeKg;
        IncomeRefills = f.Refills(f.IncomeKg);
        ExpenseKg = f.ExpenseKg;
        ExpenseRefills = f.Refills(f.ExpenseKg);
        LossKg = f.LossKg;
        LossRefills = f.Refills(f.LossKg);
        BalanceKg = f.BalanceKg;
        BalanceRefills = f.Refills(f.BalanceKg);
        NeedKg = f.NeedKg;
        NeedRefills = f.Refills(f.NeedKg);
        Autonomy = f.AutonomyDays;
        IsShortage = f.NeedKg > 0.5;
        IsNegative = f.BalanceKg < -0.5;
    }
}

/// <summary>Дозаправка ВВСТ подразделения со склада части.</summary>
public sealed partial class RefuelRow(SupplyDay day, SupplyGroup group, SupplyRow row, FuelType fuel, string supplier) : ObservableObject
{
    public SupplyGroup Group { get; } = group;
    public SupplyRow Row { get; } = row;
    public string Supplier { get; } = supplier;

    public bool IsChecked
    {
        get => day.FindRow(Row.Id, fuel)?.Refuel ?? false;
        set
        {
            if (value == IsChecked)
                return;
            day.RowEntry(Row.Id, fuel).Refuel = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Количество вручную, кг; пусто — до установленных запасов.</summary>
    public double? ManualKg
    {
        get => day.FindRow(Row.Id, fuel)?.RefuelKg;
        set
        {
            day.RowEntry(Row.Id, fuel).RefuelKg = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty] public partial double NeedKg { get; private set; }
    [ObservableProperty] public partial double ReceivedKg { get; private set; }

    public void Apply(RowResult? result)
    {
        NeedKg = result?.RefuelNeedKg ?? 0;
        ReceivedKg = result?.Income.Refuel ?? 0;
    }
}

/// <summary>Подвоз ГСМ: получение складом (вручную), выдача склада рд (рассчитывается).</summary>
public sealed partial class DeliveryRow(SupplyDay day, SupplyGroup group, SupplyRow row, FuelType fuel, bool isMain) : ObservableObject
{
    public SupplyGroup Group { get; } = group;
    public SupplyRow Row { get; } = row;
    public bool IsMain { get; } = isMain;
    public string Source => IsMain ? "от округа" : "со склада рд";

    public double ReceivedKg
    {
        get => day.FindRow(Row.Id, fuel)?.ReceivedKg ?? 0;
        set
        {
            if (Math.Abs(value - ReceivedKg) < 1e-9)
                return;
            day.RowEntry(Row.Id, fuel).ReceivedKg = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty] public partial double IssuedKg { get; private set; }

    public void Apply(RowResult? result) => IssuedKg = result?.Expense.DeliveryIssued ?? 0;
}

/// <summary>Загрузка запасов ГСМ в транспорт подвоза: склад части ↔ БМТО/РМТО.</summary>
public sealed partial class TransportRow : ObservableObject
{
    private readonly SupplyDay _day;
    private readonly FuelType _fuel;

    public TransportRow(SupplyDay day, SupplyProject project, SupplyGroup group, FuelType fuel)
    {
        _day = day;
        _fuel = fuel;
        Group = group;
        WarehouseName = project.FindRow(group.WarehouseRowId)?.Row.Name ?? "не назначен";
        TransportName = project.FindRow(group.TransportRowId)?.Row.Name ?? "не назначено";
        CapacityLiters = SupplyCalculator.TransportCapacityLiters(project, group, fuel);
        CapacityKg = SupplyCalculator.TransportCapacityKg(project, group, fuel);
    }

    public SupplyGroup Group { get; }
    public string WarehouseName { get; }
    public string TransportName { get; }
    public double CapacityLiters { get; }
    public double CapacityKg { get; }

    public bool InTransport
    {
        get => _day.FindGroup(Group.Id, _fuel)?.InTransport ?? false;
        set
        {
            if (value == InTransport)
                return;
            _day.GroupEntry(Group.Id, _fuel).InTransport = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
        }
    }

    public string StateText => InTransport ? "Топливо в транспорте" : "Топливо на складе";

    [ObservableProperty] public partial string ChangeText { get; private set; } = string.Empty;

    public void Apply(DayResult result)
    {
        var warehouse = Group.WarehouseRowId is { } w ? result.Find(w) : null;
        var transport = Group.TransportRowId is { } t ? result.Find(t) : null;
        var loaded = warehouse?.Expense.TransportIssued ?? 0;
        var unloaded = transport?.Expense.TransportIssued ?? 0;
        ChangeText = loaded > 0
            ? $"Сегодня загружено: {WarehouseName} −{loaded:N0} кг, {TransportName} +{loaded:N0} кг"
            : unloaded > 0
                ? $"Сегодня разгружено: {TransportName} −{unloaded:N0} кг, {WarehouseName} +{unloaded:N0} кг"
                : "Сегодня без изменений";
        OnPropertyChanged(nameof(StateText));
    }
}

/// <summary>Потери (вручную).</summary>
public sealed partial class LossRow(SupplyDay day, SupplyGroup group, SupplyRow row, FuelType fuel) : ObservableObject
{
    public SupplyGroup Group { get; } = group;
    public SupplyRow Row { get; } = row;

    public double LossKg
    {
        get => day.FindRow(Row.Id, fuel)?.LossKg ?? 0;
        set
        {
            if (Math.Abs(value - LossKg) < 1e-9)
                return;
            day.RowEntry(Row.Id, fuel).LossKg = value;
            OnPropertyChanged();
        }
    }
}

/// <summary>Расход на выполнение боевых задач — из отчёта модели 1 за дату суток.</summary>
public sealed partial class TaskRow(SupplyGroup group, SupplyRow row, string link) : ObservableObject
{
    public SupplyGroup Group { get; } = group;
    public SupplyRow Row { get; } = row;
    public string Link { get; } = link;

    [ObservableProperty] public partial double TasksKg { get; private set; }
    [ObservableProperty] public partial double DailyKg { get; private set; }
    [ObservableProperty] public partial double OwnMass { get; private set; }

    public void Apply(RowResult? result, double daily)
    {
        TasksKg = result?.Expense.Tasks ?? 0;
        OwnMass = result?.OwnRefillMass ?? 0;
        DailyKg = daily;
    }
}
