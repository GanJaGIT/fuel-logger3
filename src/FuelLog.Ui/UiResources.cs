using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace FuelLog.Ui;

/// <summary>Подключение общего оформления и русских форматов к приложению.</summary>
public static class UiResources
{
    private static readonly Uri StylesUri = new("pack://application:,,,/FuelLog.Ui;component/Styles.xaml");

    /// <summary>
    /// Вызывается в OnStartup до создания окон. Стили копируются в корневой словарь приложения:
    /// он имеет приоритет над словарём темы Fluent при любом порядке подключения.
    /// </summary>
    public static void Install(Application app)
    {
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        var language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(language));
        FrameworkContentElement.LanguageProperty.OverrideMetadata(typeof(System.Windows.Documents.TextElement), new FrameworkPropertyMetadata(language));

        var styles = new ResourceDictionary { Source = StylesUri };
        foreach (var key in styles.Keys)
            app.Resources[key] = styles[key];
    }
}
