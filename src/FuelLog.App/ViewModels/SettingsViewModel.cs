using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.App.Services;
using FuelLog.Core.Model;
using FuelLog.Core.Storage;

namespace FuelLog.App.ViewModels;

/// <summary>Вкладка «Настройки»: параметры проекта и программы.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ProjectSession _session;
    private readonly AppSettings _appSettings;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(ProjectSession session, AppSettings appSettings, IDialogService dialogs)
    {
        _session = session;
        _appSettings = appSettings;
        _dialogs = dialogs;
        Theme = appSettings.Theme;
        TaskTypes = new ListEditor(session.Project.Settings.TaskTypes, ProjectSettings.DefaultTaskTypes, "тип задачи", dialogs);
        Categories = new ListEditor(session.Project.Settings.Categories, ProjectSettings.DefaultCategories, "категорию", dialogs);
    }

    public Project Project => _session.Project;
    public ProjectSettings Settings => _session.Project.Settings;
    public string FilePath => _session.FilePath;
    public ListEditor TaskTypes { get; }
    public ListEditor Categories { get; }
    public AppTheme[] Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Gray, AppTheme.Dark];

    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    partial void OnThemeChanged(AppTheme value)
    {
        // При создании вкладки тема уже применена — повторно не применяем и не сохраняем.
        if (_appSettings.Theme == value && ThemeManager.Current == value)
            return;
        _appSettings.Theme = value;
        _appSettings.Save();
        ThemeManager.Apply(value);
    }

    [RelayCommand]
    private void ShowBackups()
    {
        var folder = ProjectStore.BackupFolder(_session.FilePath);
        Directory.CreateDirectory(folder);
        _dialogs.ShowInExplorer(folder);
    }

    [RelayCommand]
    private void ResetDensities()
    {
        if (!_dialogs.Confirm("Вернуть плотности по умолчанию: АБ — 0,75 кг/л, ДТ — 0,85 кг/л?"))
            return;
        Settings.PetrolDensity = 0.75;
        Settings.DieselDensity = 0.85;
    }
}

/// <summary>Редактор простого списка строк (типы задач, категории техники).</summary>
public sealed partial class ListEditor : ObservableObject
{
    private readonly string[] _defaults;
    private readonly string _noun;
    private readonly IDialogService _dialogs;

    public ListEditor(ObservableCollection<string> items, string[] defaults, string noun, IDialogService dialogs)
    {
        Items = items;
        _defaults = defaults;
        _noun = noun;
        _dialogs = dialogs;
    }

    public ObservableCollection<string> Items { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewItem { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(RenameCommand))]
    public partial string? Selected { get; set; }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewItem);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var value = NewItem.Trim();
        if (!Items.Contains(value))
            Items.Add(value);
        Selected = value;
        NewItem = string.Empty;
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Rename()
    {
        var old = Selected!;
        var value = _dialogs.Prompt("Переименование", $"Новое название:", old);
        if (value is null || value == old || Items.Contains(value))
            return;
        Items[Items.IndexOf(old)] = value;
        Selected = value;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        var index = Items.IndexOf(Selected!);
        Items.Remove(Selected!);
        Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp() => MoveSelected(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown() => MoveSelected(+1);

    private void MoveSelected(int direction)
    {
        var selected = Selected;
        Items.Move(selected!, direction);
        Selected = selected;
    }

    [RelayCommand]
    private void Reset()
    {
        if (!_dialogs.Confirm($"Заменить список стандартным набором? Добавленные вами значения будут удалены из списка (на уже созданные задачи и марки это не повлияет)."))
            return;
        Items.Clear();
        foreach (var item in _defaults)
            Items.Add(item);
    }

    public string Noun => _noun;
}
