using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.App.Services;
using FuelLog.Core.Model;
using FuelLog.Core.Storage;

namespace FuelLog.App.ViewModels;

/// <summary>Вкладка «Справочник техники»: марки, их баки и нормы расхода.</summary>
public sealed partial class CatalogViewModel : ObservableObject
{
    public const string AllCategories = "Все категории";
    private const string CatalogFilter = "Справочник техники (*.gsmtemplate)|*.gsmtemplate|Все файлы (*.*)|*.*";

    private readonly IDialogService _dialogs;

    public CatalogViewModel(Project project, IDialogService dialogs)
    {
        Project = project;
        _dialogs = dialogs;

        var view = new ListCollectionView(project.Catalog)
        {
            Filter = Matches,
            IsLiveFiltering = true,
            IsLiveSorting = true,
        };
        view.SortDescriptions.Add(new SortDescription(nameof(VehicleModel.Name), ListSortDirection.Ascending));
        view.LiveSortingProperties.Add(nameof(VehicleModel.Name));
        view.LiveFilteringProperties.Add(nameof(VehicleModel.Name));
        view.LiveFilteringProperties.Add(nameof(VehicleModel.Category));
        Models = view;

        CategoryFilter = AllCategories;
        project.Catalog.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CountText));
        project.Settings.Categories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CategoryFilters));
    }

    public Project Project { get; }
    public ICollectionView Models { get; }
    public ObservableCollection<string> Categories => Project.Settings.Categories;
    public IEnumerable<string> CategoryFilters => new[] { AllCategories }.Concat(Categories);
    public FuelType[] FuelTypes { get; } = [FuelType.Petrol, FuelType.Diesel];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CategoryFilter { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(DuplicateCommand))]
    public partial VehicleModel? SelectedModel { get; set; }

    public string CountText
    {
        get
        {
            var shown = Models.Cast<object>().Count();
            return shown == Project.Catalog.Count ? $"Марок: {shown}" : $"Показано {shown} из {Project.Catalog.Count}";
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnCategoryFilterChanged(string value) => RefreshFilter();

    private void RefreshFilter()
    {
        Models.Refresh();
        OnPropertyChanged(nameof(CountText));
    }

    private bool Matches(object obj)
    {
        if (obj is not VehicleModel m)
            return false;
        if (!string.IsNullOrWhiteSpace(CategoryFilter) && CategoryFilter != AllCategories && m.Category != CategoryFilter)
            return false;
        return string.IsNullOrWhiteSpace(SearchText)
            || m.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || m.Note.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Добавляет марку и выделяет её; представление переводит строку в режим правки.</summary>
    [RelayCommand]
    private void Add()
    {
        SearchText = string.Empty;
        var model = new VehicleModel
        {
            Name = UniqueName("Новая марка"),
            Fuel = FuelType.Diesel,
            Category = CategoryFilter != AllCategories ? CategoryFilter : string.Empty,
        };
        Project.Catalog.Add(model);
        SelectedModel = model;
        ModelAdded?.Invoke(this, model);
    }

    public event EventHandler<VehicleModel>? ModelAdded;

    private bool HasSelection() => SelectedModel is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        var source = SelectedModel!;
        var copy = new VehicleModel
        {
            Name = UniqueName(source.Name + " (копия)"),
            Fuel = source.Fuel,
            MainTankLiters = source.MainTankLiters,
            ExtraTankLiters = source.ExtraTankLiters,
            LitersPer100Km = source.LitersPer100Km,
            LitersPerHour = source.LitersPerHour,
            Category = source.Category,
            Note = source.Note,
        };
        Project.Catalog.Add(copy);
        SelectedModel = copy;
        ModelAdded?.Invoke(this, copy);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        var model = SelectedModel!;
        var usage = Project.UsageOf(model);
        var message = $"Удалить марку «{model.Name}» из справочника?";
        if (usage.Units > 0 || usage.TaskItems > 0)
            message += $"\n\nОна используется: подразделений — {usage.Units}, строк задач — {usage.TaskItems}. " +
                       "Техника этой марки будет убрана из подразделений и задач.";
        if (!_dialogs.Confirm(message))
            return;
        Project.RemoveModel(model);
        SelectedModel = null;
    }

    [RelayCommand]
    private void Import()
    {
        var path = _dialogs.OpenFile("Импорт справочника техники", CatalogFilter);
        if (path is null)
            return;
        try
        {
            var models = CatalogExchange.Read(path);
            if (models.Count == 0)
            {
                _dialogs.Info("В файле нет марок техники.");
                return;
            }
            if (!_dialogs.Confirm($"Импортировать марок: {models.Count}?\n\nМарки с совпадающим названием будут обновлены, новые — добавлены. Состав подразделений и задачи не изменятся."))
                return;
            var result = CatalogExchange.Merge(Project, models);
            OnPropertyChanged(nameof(CategoryFilters));
            RefreshFilter();
            _dialogs.Info($"Справочник обновлён.\nДобавлено: {result.Added}\nОбновлено: {result.Updated}");
        }
        catch (Exception ex) when (ex is ProjectFormatException or System.IO.IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Не удалось импортировать справочник: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Export()
    {
        var path = _dialogs.SaveFile("Экспорт справочника техники", CatalogFilter, $"Справочник техники {DateTime.Now:dd.MM.yyyy}.gsmtemplate");
        if (path is null)
            return;
        try
        {
            CatalogExchange.Export(Project.Catalog.OrderBy(m => m.Name), path);
            _dialogs.Info($"Справочник сохранён ({Project.Catalog.Count} марок):\n{path}");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Не удалось сохранить файл: {ex.Message}");
        }
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Project.Catalog.Any(m => string.Equals(m.Name, name, StringComparison.CurrentCultureIgnoreCase)); i++)
            name = $"{baseName} {i}";
        return name;
    }
}
