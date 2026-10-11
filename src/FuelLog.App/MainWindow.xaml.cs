using System.ComponentModel;
using System.Windows;
using FuelLog.App.Services;
using FuelLog.App.ViewModels;

namespace FuelLog.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Decorate(this);
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Сохраняем последние изменения (автосохранение могло ещё не сработать).
        ViewModel.CloseSession();
        base.OnClosing(e);
    }
}
