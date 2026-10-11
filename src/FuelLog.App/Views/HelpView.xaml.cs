using System.Windows;
using System.Windows.Controls;

namespace FuelLog.App.Views;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
    }

    // Переход по оглавлению: прокручиваем так, чтобы раздел оказался вверху.
    private void OnTocClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } || FindName(name) is not FrameworkElement section)
            return;
        var offset = section.TransformToAncestor(Scroller).Transform(new Point(0, 0)).Y;
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + offset - 8);
    }
}
