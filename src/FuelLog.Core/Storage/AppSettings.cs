using System.Text.Json;

namespace FuelLog.Core.Storage;

public sealed class RecentProject
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; }
}

public enum AppTheme
{
    System,
    Light,
    Dark,

    /// <summary>Графитовая: тёмная тема со средне-серыми поверхностями, мягче по контрасту.</summary>
    Gray,
}

/// <summary>Настройки программы на этом ПК (не проекта): тема, последние проекты, состояние панелей.</summary>
public sealed class AppSettings
{
    public const int MaxRecent = 12;

    public AppTheme Theme { get; set; } = AppTheme.System;
    public List<RecentProject> Recent { get; set; } = [];

    /// <summary>Раскрыта ли правая панель (сводка и добавление техники) на вкладке «Структура».</summary>
    public bool StructurePanelOpen { get; set; } = true;

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FuelLog", "settings.json");

    /// <summary>Куда сохранять настройки; задаётся при загрузке (по умолчанию — профиль пользователя).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string FilePath { get; set; } = DefaultPath;

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        var settings = new AppSettings();
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json.Options) ?? settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Повреждённые настройки не должны мешать запуску.
        }
        settings.FilePath = path;
        return settings;
    }

    public void Save(string? path = null)
    {
        try
        {
            SafeFile.WriteAllText(path ?? FilePath, JsonSerializer.Serialize(this, Json.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Список последних проектов — удобство, а не данные.
        }
    }

    public void Touch(string projectPath, string name)
    {
        var full = System.IO.Path.GetFullPath(projectPath);
        Recent.RemoveAll(r => string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, new RecentProject { Path = full, Name = name, LastOpened = DateTime.Now });
        if (Recent.Count > MaxRecent)
            Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
    }

    public void Forget(string projectPath) =>
        Recent.RemoveAll(r => string.Equals(r.Path, projectPath, StringComparison.OrdinalIgnoreCase));
}
