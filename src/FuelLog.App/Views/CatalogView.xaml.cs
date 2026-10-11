using System.Windows.Controls;
using FuelLog.App.ViewModels;
using FuelLog.Core.Model;

namespace FuelLog.App.Views;

public partial class CatalogView : UserControl
{
    public CatalogView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is CatalogViewModel oldVm)
                oldVm.ModelAdded -= OnModelAdded;
            if (e.NewValue is CatalogViewModel newVm)
                newVm.ModelAdded += OnModelAdded;
        };
    }

    // Новую марку сразу открываем на редактирование названия.
    private void OnModelAdded(object? sender, VehicleModel model)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Grid.UpdateLayout();
            Grid.ScrollIntoView(model);
            Grid.CurrentCell = new DataGridCellInfo(model, Grid.Columns[0]);
            Grid.Focus();
            Grid.BeginEdit();
        });
    }
}
