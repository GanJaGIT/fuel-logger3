using System.Windows;

namespace FuelLog.Ui;

public partial class PromptDialog : Window
{
    public PromptDialog(string title, string label, string initial)
    {
        InitializeComponent();
        ThemeManager.Decorate(this);
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
