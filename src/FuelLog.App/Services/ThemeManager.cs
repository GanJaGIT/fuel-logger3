using System.Windows;
using System.Windows.Media;
using FuelLog.Core.Storage;

namespace FuelLog.App.Services;

/// <summary>
/// Переключение темы. Светлая, тёмная и системная — встроенная тема Fluent;
/// серая — тёмная Fluent с переопределёнными цветами поверхностей.
/// </summary>
public static class ThemeManager
{
    private static readonly ResourceDictionary GrayPalette = CreateGrayPalette();

    public static AppTheme Current { get; private set; } = AppTheme.System;

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app)
            return;

        Current = theme;
        app.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark or AppTheme.Gray => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

        var merged = app.Resources.MergedDictionaries;
        merged.Remove(GrayPalette);
        if (theme == AppTheme.Gray)
            merged.Add(GrayPalette); // добавлен последним — перекрывает цвета Fluent

        foreach (Window window in app.Windows)
            Decorate(window);
    }

    /// <summary>
    /// Фон окна: в серой теме — сплошной серый, иначе — стандартный фон темы
    /// (на Windows 11 это полупрозрачный материал Mica).
    /// </summary>
    public static void Decorate(Window window)
    {
        if (Current == AppTheme.Gray)
            window.Background = (Brush)GrayPalette["ApplicationBackgroundBrush"];
        else
            window.ClearValue(Window.BackgroundProperty);
    }

    private static ResourceDictionary CreateGrayPalette()
    {
        static SolidColorBrush Solid(byte gray)
        {
            var brush = new SolidColorBrush(Color.FromRgb(gray, gray, gray));
            brush.Freeze();
            return brush;
        }

        var background = Solid(0x46);
        var popup = Solid(0x55);
        var dictionary = new ResourceDictionary
        {
            ["ApplicationBackgroundBrush"] = background,
            ["SolidBackgroundFillColorBaseBrush"] = background,
            ["SolidBackgroundFillColorSecondaryBrush"] = Solid(0x42),
            ["SolidBackgroundFillColorTertiaryBrush"] = Solid(0x4C),
            ["SolidBackgroundFillColorQuarternaryBrush"] = popup,
            ["NavigationWindowBackground"] = background,
            ["LayerOnMicaBaseAltFillColorTertiaryBrush"] = popup,
            ["ControlSolidFillColorDefaultBrush"] = Solid(0x66),
            ["CardStrokeColorDefaultSolidBrush"] = Solid(0x3A),
            ["ComboBoxDropDownBackground"] = popup,
            ["ContextMenuBackground"] = popup,
            ["DatePickerPopupBackground"] = popup,
            ["FlyoutBackground"] = popup,
            ["ToolTipBackground"] = popup,
            ["ToolTipBackgroundBrush"] = popup,
        };
        return dictionary;
    }
}
