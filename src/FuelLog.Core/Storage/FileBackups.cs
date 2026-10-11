namespace FuelLog.Core.Storage;

/// <summary>Резервные копии файлов документов перед перезаписью.</summary>
public static class FileBackups
{
    /// <summary>
    /// Копирует файл в папку резервных копий (не чаще одной копии в минуту — автосохранение срабатывает часто)
    /// и удаляет самые старые копии сверх <paramref name="keep"/>.
    /// </summary>
    public static void Create(string path, string folder, int keep)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
            File.Copy(path, Path.Combine(folder, $"{name}_{stamp}{extension}"), overwrite: true);

            var old = new DirectoryInfo(folder)
                .GetFiles($"{name}_*{extension}")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .Skip(keep);
            foreach (var file in old)
                file.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Резервная копия желательна, но её сбой не должен мешать сохранению документа.
        }
    }
}
