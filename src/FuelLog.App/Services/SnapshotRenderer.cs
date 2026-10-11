using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FuelLog.App.ViewModels;

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
            vm.AppSettings.Theme = theme switch
            {
                "dark" => FuelLog.Core.Storage.AppTheme.Dark,
                "gray" => FuelLog.Core.Storage.AppTheme.Gray,
                _ => FuelLog.Core.Storage.AppTheme.Light,
            };
            ThemeManager.Apply(vm.AppSettings.Theme);
        }
        Directory.CreateDirectory(outputFolder);
        window.Width = 1440;
        window.Height = 900;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000; // за пределами экрана
        window.Top = 0;
        window.ShowActivated = false;
        window.Show();

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

    private static void Save(Window window, string path)
    {
        // Даём WPF выполнить отложенные привязки и раскладку.
        for (var i = 0; i < 3; i++)
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(window.TryFindResource("ApplicationBackgroundBrush") as Brush ?? window.Background ?? Brushes.White, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(background);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
