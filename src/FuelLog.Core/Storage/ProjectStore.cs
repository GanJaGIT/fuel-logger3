using System.Text.Json;
using System.Text.Json.Nodes;
using FuelLog.Core.Model;

namespace FuelLog.Core.Storage;

public sealed class ProjectLoadResult
{
    public required Project Project { get; init; }

    /// <summary>Файл был в формате FuelLog 3.x и сконвертирован.</summary>
    public bool WasLegacy { get; init; }
}

public sealed class ProjectFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Чтение и запись файла проекта (*.gsmproject) с резервными копиями.</summary>
public static class ProjectStore
{
    public const string Extension = ".gsmproject";
    public const string BackupFolderName = "Резервные копии";
    public const string ReportsFolderName = "Отчёты";
    public const int BackupsToKeep = 10;

    public static ProjectLoadResult Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProjectFormatException($"Не удалось прочитать файл: {ex.Message}", ex);
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(text) as JsonObject
                ?? throw new ProjectFormatException("Файл не является проектом FuelLog.");
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException($"Файл повреждён или не является проектом FuelLog ({ex.Message}).", ex);
        }

        if (!root.ContainsKey("schemaVersion"))
            return new ProjectLoadResult { Project = LegacyImporter.Import(root, path), WasLegacy = true };

        var version = root["schemaVersion"]!.GetValue<int>();
        if (version > Project.CurrentSchemaVersion)
            throw new ProjectFormatException("Проект создан более новой версией FuelLog. Обновите программу.");

        Project project;
        try
        {
            project = root.Deserialize<Project>(Json.Options)
                ?? throw new ProjectFormatException("Пустой файл проекта.");
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException($"Ошибка в структуре файла проекта: {ex.Message}", ex);
        }

        Normalize(project, path);
        return new ProjectLoadResult { Project = project };
    }

    /// <summary>Сохраняет проект. Предыдущая версия файла уходит в папку резервных копий.</summary>
    public static void Save(Project project, string path)
    {
        project.SchemaVersion = Project.CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(project, Json.Options);

        if (File.Exists(path))
            Backup(path);

        SafeFile.WriteAllText(path, json);
    }

    public static string ReportsFolder(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, ReportsFolderName);

    public static string BackupFolder(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, BackupFolderName);

    private static void Backup(string path)
    {
        try
        {
            var folder = BackupFolder(path);
            Directory.CreateDirectory(folder);
            var name = Path.GetFileNameWithoutExtension(path);

            // Не чаще одной копии в минуту: автосохранение срабатывает часто.
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
            File.Copy(path, Path.Combine(folder, $"{name}_{stamp}{Extension}"), overwrite: true);

            var old = new DirectoryInfo(folder)
                .GetFiles($"{name}_*{Extension}")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .Skip(BackupsToKeep);
            foreach (var file in old)
                file.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Резервная копия желательна, но её сбой не должен мешать сохранению проекта.
        }
    }

    /// <summary>Восстанавливает инварианты после ручной правки файла или импорта.</summary>
    internal static void Normalize(Project project, string path)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
            project.Name = Path.GetFileNameWithoutExtension(path);

        project.Settings ??= new ProjectSettings();
        if (project.Settings.TaskTypes.Count == 0)
            foreach (var t in ProjectSettings.DefaultTaskTypes)
                project.Settings.TaskTypes.Add(t);
        if (project.Settings.Categories.Count == 0)
            foreach (var c in ProjectSettings.DefaultCategories)
                project.Settings.Categories.Add(c);
        if (project.Settings.PetrolDensity <= 0)
            project.Settings.PetrolDensity = 0.75;
        if (project.Settings.DieselDensity <= 0)
            project.Settings.DieselDensity = 0.85;
    }
}
