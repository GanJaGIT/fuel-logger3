using System.Windows;

namespace FuelLog.App.Views;

public partial class PromptDialog : Window
{
    public PromptDialog(string title, string label, string initial)
    {
        InitializeComponent();
        Services.ThemeManager.Decorate(this);
        Title = title;
        LabelText.Text = label;
        ValueBox.Text = initial;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0)
        {
            ValueBox.Focus();
            return;
        }
        DialogResult = true;
    }
}
