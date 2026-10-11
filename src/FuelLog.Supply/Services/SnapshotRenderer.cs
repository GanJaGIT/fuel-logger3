using System.IO;
using System.Windows;
using FuelLog.Core.Model;
using FuelLog.Supply.ViewModels;

namespace FuelLog.Supply.Services;

/// <summary>
/// Служебный режим «--snapshots документ папка [light|gray|dark]»: снимки всех вкладок в PNG
/// для проверки вёрстки без ручного запуска. Документ не изменяется.
/// </summary>
internal static class SnapshotRenderer
{
    public static void Run(MainWindow window, MainViewModel vm, string documentPath, string outputFolder, string? theme)
    {
        if (theme is not null)
        {
            vm.AppSettings.Theme = ThemeManager.FromName(theme);
            ThemeManager.Apply(vm.AppSettings.Theme);
        }
        Directory.CreateDirectory(outputFolder);
        WindowSnapshot.ShowOffscreen(window);
        Save(window, outputFolder, "0-start");

        if (documentPath != "-")
        {
            vm.OpenPath(documentPath);
            foreach (var tab in Enum.GetValues<MainTab>())
            {
                vm.SelectedTab = tab;
                Save(window, outputFolder, $"{(int)tab + 1}-{tab.ToString().ToLowerInvariant()}");
            }

            // Экран «Сутки»: вкладки операций и АБ.
            vm.SelectedTab = MainTab.Day;
            var day = vm.Day!;
            window.UpdateLayout();
            var tabs = FindTabControl(window);
            if (tabs is not null)
            {
                for (var i = 1; i < tabs.Items.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    window.UpdateLayout();
                    Save(window, outputFolder, $"1-day-tab{i}");
                }
                tabs.SelectedIndex = 0;
            }
            day.Fuel = FuelType.Petrol;
            Save(window, outputFolder, "1-day-petrol");

            vm.SelectedTab = MainTab.Structure;
            vm.Structure!.SelectedNode = vm.Structure.Groups.FirstOrDefault()?.Rows.Skip(1).FirstOrDefault();
            Save(window, outputFolder, "3-structure-row");
            vm.Structure.SelectedNode = vm.Structure.Groups.FirstOrDefault();
            Save(window, outputFolder, "3-structure-group");
        }

        // Документ не сохраняем: снимки не должны менять файл.
        Application.Current.Shutdown();
    }

    private static System.Windows.Controls.TabControl? FindTabControl(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.TabControl tabs)
                return tabs;
            if (FindTabControl(child) is { } found)
                return found;
        }
        return null;
    }

    private static void Save(Window window, string folder, string name) =>
        WindowSnapshot.Save(window, Path.Combine(folder, name + ".png"));
}
