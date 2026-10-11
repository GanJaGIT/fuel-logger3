using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.Core.Model;
using FuelLog.Supply.Core.Calculation;
using FuelLog.Supply.Core.Model;
using FuelLog.Supply.Services;

namespace FuelLog.Supply.ViewModels;

/// <summary>
/// «Наличие запасов» на начало первых суток. Склады — по книгам учёта (вручную);
/// подразделения — по умолчанию 1 заправка (масса заправки из модели 1).
/// </summary>
public sealed partial class StartBalancesViewModel : ObservableObject
{
    private readonly SupplySession _session;

    public StartBalancesViewModel(SupplySession session)
    {
        _session = session;
        _session.Recalculated += (_, _) => Update();
        Fuel = FuelType.Diesel;
    }

    public SupplyProject Project => _session.Project;
    public ObservableCollection<StartRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiesel), nameof(IsPetrol))]
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

    public string FirstDayText => Project.Days.Count == 0
        ? "Суток в документе пока нет — наличие будет остатком первых суток."
        : $"Это остаток на начало первых суток документа ({Project.Days[0].Date:dd.MM.yyyy}). Дальше остаток каждых суток — наличие предыдущих.";

    partial void OnFuelChanged(FuelType value) => Rebuild();

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var group in Project.Groups)
            foreach (var row in group.Rows)
                Rows.Add(new StartRow(group, row, Fuel));
        Update();
    }

    private void Update()
    {
        if (Rows.Count != Project.AllRows.Count() || Rows.Any(r => !Project.AllRows.Contains(r.Row)))
        {
            Rebuild();
            return;
        }
        var first = _session.Results(Fuel).FirstOrDefault();
        foreach (var row in Rows)
            row.Apply(first?.Find(row.Row.Id));
        OnPropertyChanged(nameof(FirstDayText));
    }

    [RelayCommand]
    private void ResetUnits()
    {
        foreach (var row in Rows.Where(r => !r.IsWarehouse))
            row.ManualKg = null;
    }
}

public sealed partial class StartRow(SupplyGroup group, SupplyRow row, FuelType fuel) : ObservableObject
{
    public SupplyGroup Group { get; } = group;
    public SupplyRow Row { get; } = row;
    public bool IsWarehouse => Row.Kind == RowKind.Warehouse;
    public string KindText => IsWarehouse ? "склад" : "подразделение";

    /// <summary>Указано вручную, кг; пусто — по умолчанию.</summary>
    public double? ManualKg
    {
        get => Row.StartKg(fuel);
        set
        {
            Row.SetStartKg(fuel, value);
            OnPropertyChanged();
        }
    }

    [ObservableProperty] public partial double DefaultKg { get; private set; }
    [ObservableProperty] public partial double StartKg { get; private set; }
    [ObservableProperty] public partial double? StartRefills { get; private set; }

    public void Apply(RowResult? result)
    {
        DefaultKg = IsWarehouse ? 0 : result?.Figures.RefillMass ?? 0;
        StartKg = result?.Figures.OpeningKg ?? ManualKg ?? DefaultKg;
        StartRefills = result?.Figures.Refills(StartKg);
    }
}
