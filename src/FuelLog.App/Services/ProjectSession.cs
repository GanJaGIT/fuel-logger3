using FuelLog.Core.Model;
using FuelLog.Core.Storage;
using FuelLog.Ui;

namespace FuelLog.App.Services;

/// <summary>Открытый проект FuelLog с автосохранением.</summary>
public sealed class ProjectSession(Project project, string filePath)
    : DocumentSession<Project>(project, filePath, ProjectStore.Save)
{
    public Project Project => Document;
    public string ReportsFolder => ProjectStore.ReportsFolder(FilePath);
}
