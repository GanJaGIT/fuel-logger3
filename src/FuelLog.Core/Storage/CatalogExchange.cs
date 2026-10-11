using System.Text.Json;
using System.Text.Json.Nodes;
using FuelLog.Core.Model;

namespace FuelLog.Core.Storage;

public sealed record CatalogMergeResult(int Added, int Updated);

/// <summary>
/// Перенос справочника техники между ПК (файл *.gsmtemplate).
/// Читает файлы как новой версии, так и FuelLog 3.x.
/// </summary>
public static class CatalogExchange
{
    public const string Extension = ".gsmtemplate";

    private sealed class CatalogFile
    {
        public int SchemaVersion { get; set; } = Project.CurrentSchemaVersion;
        public string Kind { get; set; } = "catalog";
        public List<VehicleModel> Vehicles { get; set; } = [];
    }

    public static void Export(IEnumerable<VehicleModel> models, string path)
    {
        var file = new CatalogFile { Vehicles = models.ToList() };
        SafeFile.WriteAllText(path, JsonSerializer.Serialize(file, Json.Options));
    }

    public static List<VehicleModel> Read(string path)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new ProjectFormatException("Файл не является справочником техники.");
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException($"Файл повреждён: {ex.Message}", ex);
        }

        if (!root.ContainsKey("schemaVersion"))
            return LegacyImporter.ImportTemplate(root);

        var file = root.Deserialize<CatalogFile>(Json.Options)
            ?? throw new ProjectFormatException("Пустой файл справочника.");
        return file.Vehicles;
    }

    /// <summary>
    /// Объединяет импортированные марки со справочником проекта по названию:
    /// существующие обновляются (ссылки из подразделений и задач сохраняются), новые добавляются.
    /// </summary>
    public static CatalogMergeResult Merge(Project project, IEnumerable<VehicleModel> imported)
    {
        int added = 0, updated = 0;
        foreach (var source in imported.Where(m => !string.IsNullOrWhiteSpace(m.Name)))
        {
            var target = project.Catalog.FirstOrDefault(m => string.Equals(m.Name.Trim(), source.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                target = new VehicleModel { Name = source.Name.Trim() };
                project.Catalog.Add(target);
                added++;
            }
            else
            {
                updated++;
            }

            target.Fuel = source.Fuel;
            target.MainTankLiters = source.MainTankLiters;
            target.ExtraTankLiters = source.ExtraTankLiters;
            target.LitersPer100Km = source.LitersPer100Km;
            target.LitersPerHour = source.LitersPerHour;
            target.Category = source.Category;
            target.Note = source.Note;

            if (!string.IsNullOrWhiteSpace(target.Category) && !project.Settings.Categories.Contains(target.Category))
                project.Settings.Categories.Add(target.Category);
        }
        return new CatalogMergeResult(added, updated);
    }
}
