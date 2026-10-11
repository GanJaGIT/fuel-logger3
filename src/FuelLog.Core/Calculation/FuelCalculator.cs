using FuelLog.Core.Model;

namespace FuelLog.Core.Calculation;

/// <summary>Пара значений по видам топлива: АБ и ДТ.</summary>
public readonly record struct FuelPair(double Petrol, double Diesel)
{
    public static readonly FuelPair Zero = new(0, 0);

    public static FuelPair Of(FuelType fuel, double value) =>
        fuel == FuelType.Petrol ? new(value, 0) : new(0, value);

    public double this[FuelType fuel] => fuel == FuelType.Petrol ? Petrol : Diesel;

    public static FuelPair operator +(FuelPair a, FuelPair b) => new(a.Petrol + b.Petrol, a.Diesel + b.Diesel);

    /// <summary>Поэлементное деление; при нулевом знаменателе результат 0.</summary>
    public static FuelPair operator /(FuelPair a, FuelPair b) =>
        new(b.Petrol > 0 ? a.Petrol / b.Petrol : 0, b.Diesel > 0 ? a.Diesel / b.Diesel : 0);

    public static FuelPair operator /(FuelPair a, double d) => d > 0 ? new(a.Petrol / d, a.Diesel / d) : Zero;

    public static FuelPair Sum(IEnumerable<FuelPair> values) => values.Aggregate(Zero, (acc, v) => acc + v);
}

/// <summary>Расход одной строки задачи (марка × подразделение).</summary>
public sealed record ItemCalculation(
    int Count,
    int LostCount,
    double MarchLiters,
    double WorkLiters,
    double LossLiters,
    FuelType Fuel,
    double Density)
{
    public int WorkingCount => Count - LostCount;
    public double TotalLiters => MarchLiters + WorkLiters + LossLiters;
    public double TotalKg => TotalLiters * Density;
}

/// <summary>Формулы расчёта расхода ГСМ. Без состояния, без побочных эффектов.</summary>
public static class FuelCalculator
{
    /// <summary>Число потерянных машин: N × p%, округление до целого (0,5 — вверх).</summary>
    public static int LostCount(int count, double lossPercent)
    {
        if (count <= 0 || lossPercent <= 0)
            return 0;
        var lost = (int)Math.Round(count * Math.Min(lossPercent, 100) / 100.0, MidpointRounding.AwayFromZero);
        return Math.Clamp(lost, 0, count);
    }

    /// <summary>
    /// Работающие машины расходуют топливо на марш (км/100 × норма) и на работу (ч × норма).
    /// Потерянные машины задачу не выполняют, а топливо из их баков списывается полностью.
    /// </summary>
    public static ItemCalculation Calculate(TaskItem item, VehicleModel model, ProjectSettings settings)
    {
        var count = Math.Max(item.Count, 0);
        var lost = LostCount(count, item.LossPercent);
        var working = count - lost;

        return new ItemCalculation(
            Count: count,
            LostCount: lost,
            MarchLiters: working * Math.Max(item.DistanceKm, 0) / 100.0 * model.LitersPer100Km,
            WorkLiters: working * Math.Max(item.WorkHours, 0) * model.LitersPerHour,
            LossLiters: lost * model.TankLiters,
            Fuel: model.Fuel,
            Density: settings.DensityOf(model.Fuel));
    }

    /// <summary>Масса полной заправки всей техники подразделения, кг (по видам топлива).</summary>
    public static FuelPair RefillMass(Unit unit, Project project)
    {
        var mass = FuelPair.Zero;
        foreach (var vehicle in unit.Vehicles)
        {
            var model = project.FindModel(vehicle.ModelId);
            if (model is null || vehicle.Count <= 0)
                continue;
            mass += FuelPair.Of(model.Fuel, vehicle.Count * model.TankLiters * project.Settings.DensityOf(model.Fuel));
        }
        return mass;
    }

    public static FuelPair RefillMass(Group group, Project project) =>
        FuelPair.Sum(group.Units.Select(u => RefillMass(u, project)));

    /// <summary>Объём полной заправки всей техники подразделения, л.</summary>
    public static FuelPair RefillVolume(Unit unit, Project project)
    {
        var volume = FuelPair.Zero;
        foreach (var vehicle in unit.Vehicles)
        {
            var model = project.FindModel(vehicle.ModelId);
            if (model is not null && vehicle.Count > 0)
                volume += FuelPair.Of(model.Fuel, vehicle.Count * model.TankLiters);
        }
        return volume;
    }
}
