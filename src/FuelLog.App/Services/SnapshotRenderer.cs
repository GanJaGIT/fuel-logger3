using System.IO;
using System.Windows;
using FuelLog.App.ViewModels;
using FuelLog.Ui;

namespace FuelLog.App.Services;

/// <summary>
/// Служебный режим «--snapshots проект папка»: открывает проект, по очереди показывает вкладки
/// и сохраняет снимки окна в PNG. Нужен для проверки вёрстки без ручного запуска.
/// </summary>
internal static class SnapshotRenderer
{
    public static void Run(MainWindow window, MainViewModel vm, string projectPath, string outputFolder, string? theme)
    {
        if (theme is not null)
        {
            vm.AppSettings.Theme = ThemeManager.FromName(theme);
            ThemeManager.Apply(vm.AppSettings.Theme);
        }
        Directory.CreateDirectory(outputFolder);
        WindowSnapshot.ShowOffscreen(window);

        Save(window, Path.Combine(outputFolder, "0-start.png"));

        if (projectPath != "-")
        {
            vm.OpenPath(projectPath);
            var structure = vm.Structure!;
            structure.IsPanelOpen = true;
            structure.CheckOnlyCommand.Execute(structure.Nodes.SelectMany(g => g.Units).FirstOrDefault());
            foreach (var tab in Enum.GetValues<MainTab>())
            {
                vm.SelectedTab = tab;
                Save(window, Path.Combine(outputFolder, $"{(int)tab + 1}-{tab.ToString().ToLowerInvariant()}.png"));
            }

            // Вкладка «Структура» в разных состояниях отметок и панели.
            vm.SelectedTab = MainTab.Structure;
            structure.UncheckAllCommand.Execute(null);
            Save(window, Path.Combine(outputFolder, "1-structure-none.png"));
            structure.Nodes[0].IsChecked = true;
            Save(window, Path.Combine(outputFolder, "1-structure-group-models.png"));
            structure.GroupByUnits = true;
            structure.IsPanelOpen = false;
            Save(window, Path.Combine(outputFolder, "1-structure-group-units-collapsed.png"));
            structure.IsPanelOpen = true;
            structure.GroupByUnits = false;
        }

        vm.CloseSession();
        Application.Current.Shutdown();
    }

    private static void Save(Window window, string path) => WindowSnapshot.Save(window, path);
}
