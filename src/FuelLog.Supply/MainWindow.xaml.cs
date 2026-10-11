using System.ComponentModel;
using System.Windows;
using FuelLog.Supply.ViewModels;

namespace FuelLog.Supply;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Decorate(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Сохраняем последние изменения (автосохранение могло ещё не сработать).
        ((MainViewModel)DataContext).CloseSession();
        base.OnClosing(e);
    }
}
