using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.Core.Storage;
using FuelLog.Supply.Core.Model;
using FuelLog.Supply.Core.Storage;
using FuelLog.Supply.Services;

namespace FuelLog.Supply.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SupplySession _session;
    private readonly AppSettings _appSettings;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(SupplySession session, AppSettings appSettings, IDialogService dialogs)
    {
        _session = session;
        _appSettings = appSettings;
        _dialogs = dialogs;
        Theme = appSettings.Theme;
    }

    public SupplyProject Project => _session.Project;
    public SupplySettings Settings => _session.Project.Settings;
    public string FilePath => _session.FilePath;
    public AppTheme[] Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Gray, AppTheme.Dark];

    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    partial void OnThemeChanged(AppTheme value)
    {
        if (_appSettings.Theme == value && ThemeManager.Current == value)
            return;
        _appSettings.Theme = value;
        _appSettings.Save();
        ThemeManager.Apply(value);
    }

    [RelayCommand]
    private void ShowBackups()
    {
        var folder = SupplyStore.BackupFolder(_session.FilePath);
        Directory.CreateDirectory(folder);
        _dialogs.ShowInExplorer(folder);
    }
}
