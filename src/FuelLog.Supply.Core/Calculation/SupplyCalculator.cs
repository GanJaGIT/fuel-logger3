using FuelLog.Core.Model;
using FuelLog.Supply.Core.Linking;
using FuelLog.Supply.Core.Model;

namespace FuelLog.Supply.Core.Calculation;

/// <summary>Составляющие прихода строки за сутки, кг.</summary>
public sealed record IncomeParts(double Refuel, double Delivery, double Transport)
{
    public double Total => Refuel + Delivery + Transport;
}

/// <summary>Составляющие расхода строки за сутки, кг.</summary>
public sealed record ExpenseParts(double Tasks, double RefuelIssued, double DeliveryIssued, double TransportIssued)
{
    public double Total => Tasks + RefuelIssued + DeliveryIssued + TransportIssued;
}

/// <summary>Показатели документа: З, О, Пр, R, P, Н, П (кг) и автономность (сут).</summary>
public sealed record Figures(
    double NormKg,
    double OpeningKg,
    double IncomeKg,
    double ExpenseKg,
    double LossKg,
    double DailyKg,
    double RefillMass)
{
    /// <summary>Н = О + Пр − (R + P).</summary>
    public double BalanceKg => OpeningKg + IncomeKg - (ExpenseKg + LossKg);

    /// <summary>П = З − Н.</summary>
    public double NeedKg => NormKg - BalanceKg;

    /// <summary>А = Н / среднесуточный расход; null — расхода нет.</summary>
    public double? AutonomyDays => DailyKg > 0 ? BalanceKg / DailyKg : null;

    /// <summary>Перевод в заправки по массе заправки строки; null — масса заправки не определена.</summary>
    public double? Refills(double kg) => RefillMass > 0 ? kg / RefillMass : null;
}

public sealed record RowResult(
    SupplyGroup Group,
    SupplyRow Row,
    double NormRefills,
    Figures Figures,
    IncomeParts Income,
    ExpenseParts Expense,
    double OwnRefillMass,
    double RefuelNeedKg)
{
    public bool IsWarehouse => Row.Kind == RowKind.Warehouse;
}

public sealed record GroupResult(SupplyGroup Group, IReadOnlyList<RowResult> Rows, Figures Totals);

/// <summary>Документ «Потребность и автономность» за одни сутки по одному виду топлива.</summary>
public sealed class DayResult
{
    public required SupplyDay Day { get; init; }
    public required FuelType Fuel { get; init; }
    public required IReadOnlyList<GroupResult> Groups { get; init; }
    public required Figures Total { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public RowResult? Find(Guid rowId) => Groups.SelectMany(g => g.Rows).FirstOrDefault(r => r.Row.Id == rowId);

    public IEnumerable<RowResult> AllRows => Groups.SelectMany(g => g.Rows);
}

/// <summary>
/// Расчёт документа по суткам. Остаток каждых суток — наличие предыдущих; первые сутки начинаются с «Наличия запасов».
/// <list type="bullet">
/// <item>З = Σ коэффициент × Мз области (своя техника, часть, рд, выбранные части); Мз — из модели 1.</item>
/// <item>Приход: дозаправка ВВСТ (подразделение), подвоз — получено (склад), разгрузка транспорта (склад) / загрузка (БМТО, РМТО).</item>
/// <item>Расход: на выполнение задач по модели 1 за дату, выдача на дозаправку (склад части), выдача при подвозе (склад рд),
/// загрузка в транспорт (склад) / разгрузка (БМТО, РМТО).</item>
/// <item>Н = О + Пр − (R + P); П = З − Н; А = Н / среднесуточный расход потребителей строки.</item>
/// </list>
/// </summary>
public static class SupplyCalculator
{
    /// <summary>Все сутки проекта по порядку.</summary>
    public static IReadOnlyList<DayResult> Compute(SupplyProject project, Model1Context model1, FuelType fuel)
    {
        var structure = new StructureFigures(project, model1, fuel);
        var results = new List<DayResult>();
        Dictionary<Guid, double>? previous = null;
        Dictionary<Guid, bool>? previousTransport = null;

        foreach (var day in project.Days.OrderBy(d => d.Date))
        {
            var result = ComputeDay(project, model1, fuel, structure, day, previous, previousTransport);
            results.Add(result);
            previous = result.AllRows.ToDictionary(r => r.Row.Id, r => r.Figures.BalanceKg);
            previousTransport = project.Groups.ToDictionary(g => g.Id, g => day.FindGroup(g.Id, fuel)?.InTransport ?? false);
        }
        return results;
    }

    /// <summary>Расчёт одних суток (с учётом всех предыдущих).</summary>
    public static DayResult? ComputeFor(SupplyProject project, Model1Context model1, FuelType fuel, SupplyDay day) =>
        Compute(project, model1, fuel).FirstOrDefault(r => ReferenceEquals(r.Day, day));

    /// <summary>Вместимость транспорта подвоза части по виду топлива, кг.</summary>
    public static double TransportCapacityKg(SupplyProject project, SupplyGroup group, FuelType fuel) =>
        TransportCapacityLiters(project, group, fuel) * project.Settings.DensityOf(fuel);

    public static double TransportCapacityLiters(SupplyProject project, SupplyGroup group, FuelType fuel) =>
        group.Transport.Sum(item => project.FindTransport(item.ModelId) is { } model && model.Fuel == fuel
            ? item.Count * model.CapacityLiters
            : 0);

    private static DayResult ComputeDay(
        SupplyProject project, Model1Context model1, FuelType fuel, StructureFigures s,
        SupplyDay day, Dictionary<Guid, double>? previous, Dictionary<Guid, bool>? previousTransport)
    {
        var warnings = new List<string>();
        var rows = project.AllRows.ToList();
        var main = project.FindRow(project.MainWarehouseRowId)?.Row;

        var opening = rows.ToDictionary(r => r.Id, r => previous is not null && previous.TryGetValue(r.Id, out var h) ? h : s.StartKg(r));
        var tasks = rows.ToDictionary(r => r.Id, r => r.Kind == RowKind.Unit ? model1.ConsumptionOn(r, day.Date)[fuel] : 0);
        var loss = rows.ToDictionary(r => r.Id, r => day.FindRow(r.Id, fuel)?.LossKg ?? 0);

        var refuelIn = rows.ToDictionary(r => r.Id, _ => 0.0);
        var refuelOut = rows.ToDictionary(r => r.Id, _ => 0.0);
        var deliveryIn = rows.ToDictionary(r => r.Id, _ => 0.0);
        var deliveryOut = rows.ToDictionary(r => r.Id, _ => 0.0);
        var transportIn = rows.ToDictionary(r => r.Id, _ => 0.0);
        var transportOut = rows.ToDictionary(r => r.Id, _ => 0.0);
        var refuelNeed = rows.ToDictionary(r => r.Id, _ => 0.0);

        // Подвоз: склады получают; всё, что получили склады частей, выдано со склада рд.
        foreach (var row in rows.Where(r => r.Kind == RowKind.Warehouse))
        {
            var received = day.FindRow(row.Id, fuel)?.ReceivedKg ?? 0;
            deliveryIn[row.Id] = received;
            if (main is not null && row.Id != main.Id)
                deliveryOut[main.Id] += received;
        }
        if (main is null && rows.Any(r => (day.FindRow(r.Id, fuel)?.ReceivedKg ?? 0) > 0))
            warnings.Add("Не назначен склад рд (главный склад): выдача при подвозе не учтена. Назначьте его в «Структуре».");

        // Загрузка запасов в транспорт подвоза: склад части ↔ БМТО/РМТО при смене состояния.
        foreach (var group in project.Groups)
        {
            var now = day.FindGroup(group.Id, fuel)?.InTransport ?? false;
            var before = previousTransport?.GetValueOrDefault(group.Id) ?? false;
            if (now == before)
                continue;
            if (group.WarehouseRowId is not { } warehouseId || group.TransportRowId is not { } transportId
                || !opening.ContainsKey(warehouseId) || !opening.ContainsKey(transportId))
            {
                warnings.Add($"{group.Name}: для загрузки в транспорт назначьте склад части и подразделение подвоза.");
                continue;
            }
            var amount = TransportCapacityKg(project, group, fuel);
            if (now)
            {
                transportOut[warehouseId] += amount;
                transportIn[transportId] += amount;
            }
            else
            {
                transportOut[transportId] += amount;
                transportIn[warehouseId] += amount;
            }
        }

        // Дозаправка ВВСТ: подразделение получает до установленных запасов (или указанное количество) со склада части.
        foreach (var row in rows)
        {
            var entry = day.FindRow(row.Id, fuel);
            var group = s.GroupOf(row);
            var before = opening[row.Id] + deliveryIn[row.Id] + transportIn[row.Id]
                         - tasks[row.Id] - loss[row.Id] - transportOut[row.Id];
            refuelNeed[row.Id] = Math.Max(0, s.NormKg(row) - before);

            if (entry is not { Refuel: true })
                continue;
            var supplier = s.SupplierOf(row);
            if (supplier is null)
            {
                warnings.Add($"{group.Name}, {row.Name}: не назначен склад части — дозаправка не выполнена.");
                continue;
            }
            var amount = Math.Max(0, entry.RefuelKg ?? refuelNeed[row.Id]);
            refuelIn[row.Id] += amount;
            refuelOut[supplier.Id] += amount;
        }

        var groupResults = new List<GroupResult>();
        foreach (var group in project.Groups)
        {
            var rowResults = new List<RowResult>();
            foreach (var row in group.Rows)
            {
                var income = new IncomeParts(refuelIn[row.Id], deliveryIn[row.Id], transportIn[row.Id]);
                var expense = new ExpenseParts(tasks[row.Id], refuelOut[row.Id], deliveryOut[row.Id], transportOut[row.Id]);
                var figures = new Figures(s.NormKg(row), opening[row.Id], income.Total, expense.Total, loss[row.Id],
                    s.DailyKg(row), s.EffectiveMass(row));
                rowResults.Add(new RowResult(group, row, row.NormRefills, figures, income, expense, s.OwnMass(row), refuelNeed[row.Id]));

                if (row.Kind == RowKind.Warehouse && figures.BalanceKg < -0.5)
                    warnings.Add($"{group.Name}, {row.Name}: наличие отрицательное ({figures.BalanceKg:N0} кг) — выдано больше, чем было на складе.");
            }
            groupResults.Add(new GroupResult(group, rowResults, Sum(rowResults, s.GroupMass(group), s.GroupDailyKg(group))));
        }

        foreach (var row in rows.Where(r => r.IsLinked && model1.FindUnit(r) is null))
        {
            var model = model1.ModelOf(row);
            warnings.Add(model is { IsLoaded: false }
                ? $"{s.GroupOf(row).Name}, {row.Name}: проект модели 1 «{model.Link.Name}» недоступен ({model.Error})."
                : $"{s.GroupOf(row).Name}, {row.Name}: подразделение удалено из проекта модели 1 — привяжите строку заново.");
        }

        var all = groupResults.SelectMany(g => g.Rows).ToList();
        return new DayResult
        {
            Day = day,
            Fuel = fuel,
            Groups = groupResults,
            Total = Sum(all, s.DivisionMass, s.DivisionDailyKg),
            Warnings = warnings.Distinct().ToList(),
        };
    }

    private static Figures Sum(IReadOnlyList<RowResult> rows, double mass, double dailyKg) => new(
        rows.Sum(r => r.Figures.NormKg),
        rows.Sum(r => r.Figures.OpeningKg),
        rows.Sum(r => r.Figures.IncomeKg),
        rows.Sum(r => r.Figures.ExpenseKg),
        rows.Sum(r => r.Figures.LossKg),
        dailyKg,
        mass);
}

/// <summary>Величины структуры, не зависящие от суток: массы заправки, нормы, потребители.</summary>
internal sealed class StructureFigures
{
    private readonly SupplyProject _project;
    private readonly FuelType _fuel;
    private readonly Dictionary<Guid, double> _own = [];
    private readonly Dictionary<Guid, double> _daily = [];
    private readonly Dictionary<Guid, SupplyGroup> _groupOf = [];
    private readonly Dictionary<Guid, double> _groupMass = [];
    private readonly Dictionary<Guid, double> _groupDaily = [];

    public StructureFigures(SupplyProject project, Model1Context model1, FuelType fuel)
    {
        _project = project;
        _fuel = fuel;
        foreach (var group in project.Groups)
        {
            foreach (var row in group.Rows)
            {
                _groupOf[row.Id] = group;
                _own[row.Id] = row.Kind == RowKind.Unit ? model1.RefillMass(row)[fuel] : 0;
                _daily[row.Id] = row.Kind == RowKind.Unit ? model1.DailyAverage(row)[fuel] : 0;
            }
            _groupMass[group.Id] = group.Rows.Sum(r => _own[r.Id]);
            _groupDaily[group.Id] = group.Rows.Sum(r => _daily[r.Id]);
        }
        DivisionMass = _groupMass.Values.Sum();
        DivisionDailyKg = _groupDaily.Values.Sum();
    }

    public double DivisionMass { get; }
    public double DivisionDailyKg { get; }

    public SupplyGroup GroupOf(SupplyRow row) => _groupOf[row.Id];
    public double OwnMass(SupplyRow row) => _own[row.Id];
    public double GroupMass(SupplyGroup group) => _groupMass[group.Id];
    public double GroupDailyKg(SupplyGroup group) => _groupDaily[group.Id];

    private double ScopeMass(SupplyRow row, NormTerm term) => term.Scope switch
    {
        NormScope.Own => _own[row.Id],
        NormScope.Group => _groupMass[GroupOf(row).Id],
        NormScope.Division => DivisionMass,
        NormScope.SelectedGroups => term.GroupIds.Sum(id => _groupMass.GetValueOrDefault(id)),
        _ => 0,
    };

    /// <summary>З, кг.</summary>
    public double NormKg(SupplyRow row) => row.Norm.Sum(t => t.Coefficient * ScopeMass(row, t));

    /// <summary>Масса одной «заправки» строки для перевода кг ↔ запр: З(кг) / З(запр); для склада без нормы — Мз части.</summary>
    public double EffectiveMass(SupplyRow row)
    {
        var refills = row.NormRefills;
        if (refills > 0)
            return NormKg(row) / refills;
        if (row.Kind == RowKind.Warehouse)
            return row.Id == _project.MainWarehouseRowId ? DivisionMass : _groupMass[GroupOf(row).Id];
        return _own[row.Id];
    }

    /// <summary>Наличие на начало первых суток: указанное вручную или 1 заправка строки (склад — 0).</summary>
    public double StartKg(SupplyRow row) => row.StartKg(_fuel) ?? (row.Kind == RowKind.Unit ? EffectiveMass(row) : 0);

    /// <summary>Склад, с которого дозаправляется подразделение: склад части, иначе склад рд.</summary>
    public SupplyRow? SupplierOf(SupplyRow row)
    {
        var group = GroupOf(row);
        var supplier = _project.FindRow(group.WarehouseRowId)?.Row ?? _project.FindRow(_project.MainWarehouseRowId)?.Row;
        return supplier is null || supplier.Id == row.Id ? null : supplier;
    }

    /// <summary>
    /// Среднесуточный расход потребителей строки, кг/сут: для подразделения — свой; для склада и подразделения
    /// подвоза — по области нормы (часть, рд); склад без нормы обеспечивает свою часть (склад рд — всю рд).
    /// </summary>
    public double DailyKg(SupplyRow row)
    {
        var group = GroupOf(row);
        var scopes = row.Norm.Select(t => t.Scope).Distinct().ToList();
        if (scopes.Count == 0)
        {
            if (row.Kind == RowKind.Unit)
                return _daily[row.Id];
            return row.Id == _project.MainWarehouseRowId ? DivisionDailyKg : _groupDaily[group.Id];
        }
        if (scopes.Contains(NormScope.Division))
            return DivisionDailyKg;

        var consumers = new HashSet<Guid>();
        foreach (var term in row.Norm)
        {
            switch (term.Scope)
            {
                case NormScope.Own:
                    consumers.Add(row.Id);
                    break;
                case NormScope.Group:
                    foreach (var r in group.Rows)
                        consumers.Add(r.Id);
                    break;
                case NormScope.SelectedGroups:
                    foreach (var r in _project.Groups.Where(g => term.GroupIds.Contains(g.Id)).SelectMany(g => g.Rows))
                        consumers.Add(r.Id);
                    break;
            }
        }
        return consumers.Sum(id => _daily.GetValueOrDefault(id));
    }
}
