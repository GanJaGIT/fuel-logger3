using FuelLog.Core.Model;

namespace FuelLog.Core.Calculation;

/// <summary>Итоговые показатели строки или группы строк отчёта.</summary>
public sealed class ReportFigures
{
    public ReportFigures(FuelPair kg, FuelPair mass, IEnumerable<DateOnly> dates)
    {
        Kg = kg;
        Mass = mass;
        Dates = dates.Distinct().Order().ToList();
    }

    /// <summary>Расход, кг.</summary>
    public FuelPair Kg { get; }

    /// <summary>Масса полной заправки техники этого уровня, кг.</summary>
    public FuelPair Mass { get; }

    /// <summary>Даты выполнения задач, вошедших в строку.</summary>
    public IReadOnlyList<DateOnly> Dates { get; }

    /// <summary>Расход в заправках: кг / масса заправки.</summary>
    public FuelPair Refills => Kg / Mass;

    /// <summary>Среднесуточный расход в заправках: заправки / число дней.</summary>
    public FuelPair DailyRefills => Refills / Dates.Count;

    public string DateLabel => Dates.Count switch
    {
        0 => string.Empty,
        1 => Dates[0].ToString("dd.MM.yyyy"),
        _ => $"{Dates[0]:dd.MM.yyyy} – {Dates[^1]:dd.MM.yyyy}",
    };
}

public sealed record ReportRow(Group Group, Unit Unit, FuelTask Task, ReportFigures Figures);

public sealed record UnitSection(Group Group, Unit Unit, IReadOnlyList<ReportRow> Rows, ReportFigures Figures);

public sealed record GroupSection(Group Group, IReadOnlyList<UnitSection> Units, ReportFigures Figures);

/// <summary>Расчёт одной строки задачи — для листа детализации.</summary>
public sealed record DetailLine(FuelTask Task, Group Group, Unit Unit, VehicleModel Model, TaskItem Item, ItemCalculation Calc);

public sealed class ReportOptions
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Учитывать только задачи с отметкой «в расчёт».</summary>
    public bool OnlyIncluded { get; init; } = true;

    public bool Accepts(FuelTask task) =>
        (!OnlyIncluded || task.IsIncluded)
        && (From is null || task.Date >= From)
        && (To is null || task.Date <= To);
}

public sealed class FuelReport
{
    public required string ProjectName { get; init; }
    public required DateTime GeneratedAt { get; init; }
    public required double PetrolDensity { get; init; }
    public required double DieselDensity { get; init; }
    public required int TaskCount { get; init; }
    public required IReadOnlyList<GroupSection> Groups { get; init; }
    public required ReportFigures Total { get; init; }
    public required IReadOnlyList<DetailLine> Details { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public bool IsEmpty => Groups.Count == 0;

    public static FuelReport Build(Project project, ReportOptions? options = null, DateTime? now = null)
    {
        options ??= new ReportOptions();
        var warnings = new List<string>();
        var details = new List<DetailLine>();

        var tasks = project.Tasks
            .Select((task, index) => (task, index))
            .Where(t => options.Accepts(t.task))
            .OrderBy(t => t.task.Date)
            .ThenBy(t => t.index)
            .Select(t => t.task)
            .ToList();

        // Расход по паре (подразделение, задача).
        var kgByUnitTask = new Dictionary<(Guid Unit, Guid Task), FuelPair>();

        foreach (var task in tasks)
        {
            foreach (var item in task.Items)
            {
                var location = project.FindUnit(item.UnitId);
                var model = project.FindModel(item.ModelId);
                if (location is null || model is null)
                {
                    warnings.Add($"Задача «{task.Name}» ({task.Date:dd.MM.yyyy}): строка ссылается на удалённое подразделение или марку и пропущена.");
                    continue;
                }

                var (group, unit) = location.Value;
                var available = unit.Vehicles.Where(v => v.ModelId == model.Id).Sum(v => v.Count);
                if (item.Count > available)
                    warnings.Add($"Задача «{task.Name}» ({task.Date:dd.MM.yyyy}): {unit.Name}, {model.Name} — задействовано {item.Count}, а в наличии {available}.");

                var calc = FuelCalculator.Calculate(item, model, project.Settings);
                details.Add(new DetailLine(task, group, unit, model, item, calc));

                var key = (unit.Id, task.Id);
                kgByUnitTask[key] = kgByUnitTask.GetValueOrDefault(key) + FuelPair.Of(calc.Fuel, calc.TotalKg);
            }
        }

        var groupSections = new List<GroupSection>();
        foreach (var group in project.Groups)
        {
            var unitSections = new List<UnitSection>();
            foreach (var unit in group.Units)
            {
                var unitMass = FuelCalculator.RefillMass(unit, project);
                var rows = tasks
                    .Where(task => kgByUnitTask.ContainsKey((unit.Id, task.Id)))
                    .Select(task => new ReportRow(group, unit, task,
                        new ReportFigures(kgByUnitTask[(unit.Id, task.Id)], unitMass, [task.Date])))
                    .ToList();

                if (rows.Count == 0)
                    continue;

                unitSections.Add(new UnitSection(group, unit, rows, new ReportFigures(
                    FuelPair.Sum(rows.Select(r => r.Figures.Kg)), unitMass, rows.Select(r => r.Task.Date))));
            }

            if (unitSections.Count == 0)
                continue;

            groupSections.Add(new GroupSection(group, unitSections, new ReportFigures(
                FuelPair.Sum(unitSections.Select(u => u.Figures.Kg)),
                FuelCalculator.RefillMass(group, project),
                unitSections.SelectMany(u => u.Figures.Dates))));
        }

        var total = new ReportFigures(
            FuelPair.Sum(groupSections.Select(g => g.Figures.Kg)),
            FuelPair.Sum(groupSections.Select(g => g.Figures.Mass)),
            groupSections.SelectMany(g => g.Figures.Dates));

        foreach (var section in groupSections.SelectMany(g => g.Units))
        {
            foreach (var fuel in new[] { FuelType.Petrol, FuelType.Diesel })
            {
                if (section.Figures.Kg[fuel] > 0 && section.Figures.Mass[fuel] <= 0)
                    warnings.Add($"{section.Group.Name}, {section.Unit.Name}: есть расход {fuel.ShortName()}, но масса заправки {fuel.ShortName()} равна нулю — заправки не рассчитаны. Проверьте состав техники подразделения.");
            }
        }

        return new FuelReport
        {
            ProjectName = project.Name,
            GeneratedAt = now ?? DateTime.Now,
            PetrolDensity = project.Settings.PetrolDensity,
            DieselDensity = project.Settings.DieselDensity,
            TaskCount = tasks.Count,
            Groups = groupSections,
            Total = total,
            Details = details,
            Warnings = warnings,
        };
    }
}
