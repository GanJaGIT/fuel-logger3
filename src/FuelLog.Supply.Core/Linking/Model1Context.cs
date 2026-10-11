using FuelLog.Core.Calculation;
using FuelLog.Core.Model;
using FuelLog.Core.Storage;
using FuelLog.Supply.Core.Model;

namespace FuelLog.Supply.Core.Linking;

/// <summary>Прочитанный проект FuelLog (модель 1) — только для чтения, файл не изменяется.</summary>
public sealed class LinkedModel
{
    public required LinkedProject Link { get; init; }
    public required string FullPath { get; init; }
    public Project? Project { get; init; }
    public string? Error { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }

    /// <summary>Расход по подразделению и дате, кг: (unitId, date) → АБ/ДТ.</summary>
    internal Dictionary<(Guid Unit, DateOnly Date), FuelPair> Consumption { get; } = [];

    /// <summary>Среднесуточный расход подразделения по отчёту модели 1, кг/сут.</summary>
    internal Dictionary<Guid, FuelPair> DailyAverage { get; } = [];

    public bool IsLoaded => Project is not null;
}

/// <summary>
/// Данные модели 1 для расчёта модели 2: масса заправки подразделений, расход на выполнение задач по датам
/// и среднесуточный расход. Берутся только задачи, отмеченные «в расчёт».
/// </summary>
public sealed class Model1Context
{
    private readonly Dictionary<Guid, LinkedModel> _byLink = [];

    private Model1Context(IReadOnlyList<LinkedModel> models)
    {
        Models = models;
        foreach (var model in models)
            _byLink[model.Link.Id] = model;
    }

    public IReadOnlyList<LinkedModel> Models { get; }

    public static Model1Context Empty { get; } = new([]);

    public static string ResolvePath(LinkedProject link, string supplyFilePath)
    {
        if (Path.IsPathRooted(link.Path))
            return Path.GetFullPath(link.Path);
        var folder = Path.GetDirectoryName(Path.GetFullPath(supplyFilePath))!;
        return Path.GetFullPath(Path.Combine(folder, link.Path));
    }

    /// <summary>Путь для хранения: относительный к файлу модели 2, если файлы на одном диске.</summary>
    public static string StoredPath(string fullPath, string supplyFilePath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(supplyFilePath))!;
        var relative = Path.GetRelativePath(folder, fullPath);
        return Path.IsPathRooted(relative) ? fullPath : relative;
    }

    public static Model1Context Load(SupplyProject project, string supplyFilePath)
    {
        var models = new List<LinkedModel>();
        foreach (var link in project.Links)
            models.Add(LoadOne(link, ResolvePath(link, supplyFilePath)));
        return new Model1Context(models);
    }

    /// <summary>Перечитывает только изменившиеся файлы.</summary>
    public Model1Context Refresh(SupplyProject project, string supplyFilePath)
    {
        var models = new List<LinkedModel>();
        foreach (var link in project.Links)
        {
            var path = ResolvePath(link, supplyFilePath);
            var current = _byLink.GetValueOrDefault(link.Id);
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
            models.Add(current is not null && current.FullPath == path && current.LastWriteTimeUtc == stamp && current.IsLoaded
                ? current
                : LoadOne(link, path));
        }
        return new Model1Context(models);
    }

    public bool IsStale(SupplyProject project, string supplyFilePath)
    {
        if (project.Links.Count != Models.Count)
            return true;
        foreach (var link in project.Links)
        {
            var path = ResolvePath(link, supplyFilePath);
            if (!_byLink.TryGetValue(link.Id, out var model) || model.FullPath != path)
                return true;
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
            if (stamp != model.LastWriteTimeUtc)
                return true;
        }
        return false;
    }

    internal static LinkedModel LoadOne(LinkedProject link, string path)
    {
        if (!File.Exists(path))
            return new LinkedModel { Link = link, FullPath = path, Error = "Файл не найден" };

        Project project;
        try
        {
            project = ProjectStore.Load(path).Project;
        }
        catch (ProjectFormatException ex)
        {
            return new LinkedModel { Link = link, FullPath = path, Error = ex.Message };
        }

        var model = new LinkedModel
        {
            Link = link,
            FullPath = path,
            Project = project,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(path),
        };

        var report = FuelReport.Build(project, new ReportOptions { OnlyIncluded = true });
        foreach (var line in report.Details)
        {
            var key = (line.Unit.Id, line.Task.Date);
            model.Consumption[key] = model.Consumption.GetValueOrDefault(key) + FuelPair.Of(line.Calc.Fuel, line.Calc.TotalKg);
        }
        foreach (var section in report.Groups.SelectMany(g => g.Units))
            model.DailyAverage[section.Unit.Id] = section.Figures.Kg / section.Figures.Dates.Count;

        if (link.Name != project.Name)
            link.Name = project.Name;
        return model;
    }

    public LinkedModel? ModelOf(SupplyRow row) =>
        row.LinkProjectId is { } id ? _byLink.GetValueOrDefault(id) : null;

    public (LinkedModel Model, Group Group, Unit Unit)? FindUnit(SupplyRow row)
    {
        if (ModelOf(row) is not { Project: { } project } model || row.LinkUnitId is not { } unitId)
            return null;
        return project.FindUnit(unitId) is { } found ? (model, found.Group, found.Unit) : null;
    }

    /// <summary>Масса полной заправки техники подразделения (Мз), кг — с плотностями проекта модели 1.</summary>
    public FuelPair RefillMass(SupplyRow row) =>
        FindUnit(row) is { } found ? FuelCalculator.RefillMass(found.Unit, found.Model.Project!) : FuelPair.Zero;

    /// <summary>Расход подразделения на выполнение задач за дату по модели 1, кг.</summary>
    public FuelPair ConsumptionOn(SupplyRow row, DateOnly date) =>
        ModelOf(row) is { } model && row.LinkUnitId is { } unitId
            ? model.Consumption.GetValueOrDefault((unitId, date))
            : FuelPair.Zero;

    /// <summary>Среднесуточный расход подразделения по отчёту модели 1, кг/сут.</summary>
    public FuelPair DailyAverage(SupplyRow row) =>
        ModelOf(row) is { } model && row.LinkUnitId is { } unitId
            ? model.DailyAverage.GetValueOrDefault(unitId)
            : FuelPair.Zero;

    /// <summary>Даты задач во всех привязанных проектах (для подсказки, на какие сутки есть расход).</summary>
    public IReadOnlyList<DateOnly> TaskDates() =>
        Models.SelectMany(m => m.Consumption.Keys.Select(k => k.Date)).Distinct().Order().ToList();

    /// <summary>Все подразделения привязанных проектов — для выбора привязки строки.</summary>
    public IEnumerable<UnitOption> UnitOptions() =>
        from model in Models
        let project = model.Project
        where project is not null
        from g in project.Groups
        from u in g.Units
        select new UnitOption(model.Link.Id, u.Id, project.Name, g.Name, u.Name);
}

public sealed record UnitOption(Guid ProjectId, Guid UnitId, string ProjectName, string GroupName, string UnitName)
{
    public string Display => $"{GroupName} — {UnitName}";
    public string DisplayWithProject => $"{GroupName} — {UnitName}  ({ProjectName})";
}
