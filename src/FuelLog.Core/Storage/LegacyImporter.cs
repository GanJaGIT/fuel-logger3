using System.Text.Json;
using System.Text.Json.Nodes;
using FuelLog.Core.Model;

namespace FuelLog.Core.Storage;

/// <summary>
/// Импорт проектов FuelLog 3.x: файл *.gsmproject плюс Data/units.json,
/// Settings/projectsettings.json и Settings/templates/*.gsmtemplate рядом с ним.
/// Задачи в 3.x не сохранялись, поэтому переносятся справочник, структура и настройки.
/// </summary>
internal static class LegacyImporter
{
    // Порядок значений перечисления UnitType в 3.x (категории техники хранились числом).
    private static readonly string[] LegacyCategoryNames =
    [
        "1 РП", "2 РП", "3 РП", "4 РП", "ТРБ", "РТБ", "ПОО",
        "РВО", "АС", "НС", "РХБЗ", "ПС", "ВС", "ГСМ", "Мед Сл", "ППЗ и СР", "Связь", "Другое",
    ];

    public static Project Import(JsonObject root, string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var project = new Project
        {
            Name = Str(root, "Name") is { Length: > 0 } name ? name : Path.GetFileNameWithoutExtension(path),
        };

        var settings = ReadSidecar(Path.Combine(folder, "Settings", "projectsettings.json")) ?? Obj(root, "Settings");
        if (settings is not null)
            ImportSettings(settings, project.Settings);

        // Справочник: шаблоны из папки имеют приоритет над копией внутри файла проекта.
        var legacyIdToModel = new Dictionary<int, VehicleModel>();
        var templateVehicles = ReadTemplateFolder(Path.Combine(folder, "Settings", "templates"));
        if (templateVehicles.Count == 0 && Arr(root, "Templates") is { } templates)
            templateVehicles = templates.OfType<JsonObject>().SelectMany(t => Arr(t, "Vehicles")?.OfType<JsonObject>() ?? []).ToList();

        foreach (var v in templateVehicles)
        {
            var model = ImportVehicle(v);
            if (project.Catalog.Any(m => string.Equals(m.Name, model.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            project.Catalog.Add(model);
            legacyIdToModel.TryAdd(Int(v, "Id"), model);
        }

        var units = ReadSidecarArray(Path.Combine(folder, "Data", "units.json")) ?? Arr(root, "Units");
        foreach (var node in units?.OfType<JsonObject>() ?? [])
        {
            var group = new Group { Name = Str(node, "Name") };
            if (Arr(node, "Vehicles") is { Count: > 0 })
                group.Units.Add(ImportUnit(node, group.Name, project, legacyIdToModel));
            foreach (var child in Arr(node, "Children")?.OfType<JsonObject>() ?? [])
                FlattenUnits(child, prefix: null, group, project, legacyIdToModel);
            project.Groups.Add(group);
        }

        ProjectStore.Normalize(project, path);
        return project;
    }

    /// <summary>Импорт справочника из файла *.gsmtemplate версии 3.x.</summary>
    public static List<VehicleModel> ImportTemplate(JsonObject root) =>
        (Arr(root, "Vehicles")?.OfType<JsonObject>() ?? []).Select(ImportVehicle).ToList();

    private static void FlattenUnits(JsonObject node, string? prefix, Group group, Project project, Dictionary<int, VehicleModel> map)
    {
        var name = prefix is null ? Str(node, "Name") : $"{prefix} / {Str(node, "Name")}";
        var children = Arr(node, "Children")?.OfType<JsonObject>().ToList() ?? [];
        if (children.Count == 0 || Arr(node, "Vehicles") is { Count: > 0 })
            group.Units.Add(ImportUnit(node, name, project, map));
        foreach (var child in children)
            FlattenUnits(child, name, group, project, map);
    }

    private static Unit ImportUnit(JsonObject node, string name, Project project, Dictionary<int, VehicleModel> map)
    {
        var unit = new Unit { Name = name };
        foreach (var v in Arr(node, "Vehicles")?.OfType<JsonObject>() ?? [])
        {
            var vehicleName = Str(v, "Name");
            var model = map.GetValueOrDefault(Int(v, "Id"));
            if (model is null || !string.Equals(model.Name, vehicleName, StringComparison.OrdinalIgnoreCase))
                model = project.Catalog.FirstOrDefault(m => string.Equals(m.Name, vehicleName, StringComparison.OrdinalIgnoreCase));
            if (model is null)
            {
                // Марку удалили из шаблона, но она осталась в подразделении — восстанавливаем из копии параметров.
                model = ImportVehicle(v);
                project.Catalog.Add(model);
            }

            var existing = unit.Vehicles.FirstOrDefault(x => x.ModelId == model.Id);
            if (existing is not null)
                existing.Count += Int(v, "Count");
            else
                unit.Vehicles.Add(new UnitVehicle { ModelId = model.Id, Count = Int(v, "Count") });
        }
        return unit;
    }

    private static VehicleModel ImportVehicle(JsonObject v)
    {
        var categoryIndex = Int(v, "Category", -1);
        return new VehicleModel
        {
            Name = Str(v, "Name"),
            Fuel = Int(v, "FuelType") == 1 || Str(v, "FuelType") == "Diesel" ? FuelType.Diesel : FuelType.Petrol,
            MainTankLiters = Dbl(v, "FuelSupplyMain"),
            ExtraTankLiters = Dbl(v, "FuelSupplyBank"),
            LitersPer100Km = Dbl(v, "FuelConsumptionPer100Km"),
            LitersPerHour = Dbl(v, "FuelConsumptionPerHour"),
            Category = categoryIndex >= 0 && categoryIndex < LegacyCategoryNames.Length ? LegacyCategoryNames[categoryIndex] : string.Empty,
            Note = Str(v, "Description"),
        };
    }

    private static void ImportSettings(JsonObject node, ProjectSettings settings)
    {
        if (Dbl(node, "PetrolDensity") is > 0 and var petrol)
            settings.PetrolDensity = petrol;
        if (Dbl(node, "DieselDensity") is > 0 and var diesel)
            settings.DieselDensity = diesel;

        if (Arr(node, "CustomTaskTypes") is { Count: > 0 } types)
        {
            settings.TaskTypes.Clear();
            foreach (var t in types.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
                settings.TaskTypes.Add(t!);
        }
    }

    private static List<JsonObject> ReadTemplateFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*.gsmtemplate")
            .Select(ReadSidecar)
            .Where(t => t is not null)
            .SelectMany(t => Arr(t!, "Vehicles")?.OfType<JsonObject>() ?? [])
            .ToList();
    }

    private static JsonObject? ReadSidecar(string path) => ReadNode(path) as JsonObject;

    private static JsonArray? ReadSidecarArray(string path) => ReadNode(path) as JsonArray;

    private static JsonNode? ReadNode(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // В 3.x имена свойств в PascalCase, но сериализатор читал их без учёта регистра — делаем так же.
    private static JsonNode? Prop(JsonObject obj, string name) =>
        obj.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static JsonObject? Obj(JsonObject obj, string name) => Prop(obj, name) as JsonObject;

    private static JsonArray? Arr(JsonObject obj, string name) => Prop(obj, name) as JsonArray;

    private static string Str(JsonObject obj, string name) =>
        Prop(obj, name) is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : string.Empty;

    private static int Int(JsonObject obj, string name, int fallback = 0) =>
        Prop(obj, name) is JsonValue v && v.TryGetValue<int>(out var i) ? i : fallback;

    private static double Dbl(JsonObject obj, string name) =>
        Prop(obj, name) is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
}
