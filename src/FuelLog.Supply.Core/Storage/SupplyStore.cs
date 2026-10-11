using System.Text.Json;
using System.Text.Json.Nodes;
using FuelLog.Core.Storage;
using FuelLog.Supply.Core.Model;

namespace FuelLog.Supply.Core.Storage;

/// <summary>Чтение и запись файла модели 2 (*.gsmsupply) с резервными копиями.</summary>
public static class SupplyStore
{
    public const string Extension = ".gsmsupply";
    public const string BackupFolderName = "Резервные копии";
    public const string ReportsFolderName = "Отчёты";
    public const int BackupsToKeep = 10;

    public static SupplyProject Load(string path)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new ProjectFormatException("Файл не является документом «Потребность и автономность».");
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException($"Файл повреждён ({ex.Message}).", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProjectFormatException($"Не удалось прочитать файл: {ex.Message}", ex);
        }

        // У проекта FuelLog тоже есть schemaVersion и groups; отличаем по полям модели 2.
        if (root["schemaVersion"]?.GetValue<int>() is not { } version || root["links"] is null || root["days"] is null)
            throw new ProjectFormatException("Файл не является документом «Потребность и автономность» (возможно, это проект FuelLog — его нужно привязать, а не открыть).");
        if (version > SupplyProject.CurrentSchemaVersion)
            throw new ProjectFormatException("Документ создан более новой версией программы. Обновите программу.");

        SupplyProject project;
        try
        {
            project = root.Deserialize<SupplyProject>(Json.Options) ?? throw new ProjectFormatException("Пустой файл.");
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException($"Ошибка в структуре файла: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(project.Name))
            project.Name = Path.GetFileNameWithoutExtension(path);
        return project;
    }

    public static void Save(SupplyProject project, string path)
    {
        project.SchemaVersion = SupplyProject.CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(project, Json.Options);
        if (File.Exists(path))
            FileBackups.Create(path, BackupFolder(path), BackupsToKeep);
        SafeFile.WriteAllText(path, json);
    }

    public static string ReportsFolder(string path) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ReportsFolderName);

    public static string BackupFolder(string path) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, BackupFolderName);
}
