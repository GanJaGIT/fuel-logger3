using System.Windows;
using System.Windows.Controls;
using FuelLog.Supply.ViewModels;

namespace FuelLog.Supply.Views;

public partial class StructureView : UserControl
{
    public StructureView()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as StructureViewModel)?.Refresh();
    }

    // SelectedItem у TreeView только для чтения — передаём выбор во ViewModel.
    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is StructureViewModel vm)
            vm.SelectedNode = e.NewValue;
    }

    private void OnScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is StructureViewModel vm && sender is FrameworkElement { DataContext: var term })
        {
            vm.SelectedTerm = term as Supply.Core.Model.NormTerm;
            vm.OnTermScopeChanged();
        }
    }
}
