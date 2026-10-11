using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FuelLog.Ui;

/// <summary>Снимок содержимого окна в PNG — для проверки вёрстки в служебном режиме «--snapshots».</summary>
public static class WindowSnapshot
{
    /// <summary>Показывает окно за пределами экрана в заданном размере.</summary>
    public static void ShowOffscreen(Window window, double width = 1440, double height = 900)
    {
        window.Width = width;
        window.Height = height;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = 0;
        window.ShowActivated = false;
        window.Show();
    }

    public static void Save(Window window, string path)
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
            dc.DrawRectangle(window.TryFindResource("ApplicationBackgroundBrush") as Brush ?? window.Background ?? Brushes.White,
                null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(background);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
