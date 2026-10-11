using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.Core.Model;
using FuelLog.Supply.Core.Calculation;
using FuelLog.Supply.Core.Model;
using FuelLog.Supply.Services;

namespace FuelLog.Supply.ViewModels;

/// <summary>Вкладка «Транспорт подвоза»: типовой транспорт и наличие транспорта у БМТО/РМТО частей.</summary>
public sealed partial class TransportViewModel : ObservableObject
{
    private readonly SupplySession _session;
    private readonly IDialogService _dialogs;

    public TransportViewModel(SupplySession session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        _session.Recalculated += (_, _) => UpdateTotals();
        SelectedGroup = Project.Groups.FirstOrDefault(g => g.TransportRowId is not null) ?? Project.Groups.FirstOrDefault();
    }

    public SupplyProject Project => _session.Project;
    public ObservableCollection<TransportModel> Catalog => Project.TransportCatalog;
    public ObservableCollection<SupplyGroup> Groups => Project.Groups;
    public FuelType[] FuelTypes { get; } = [FuelType.Diesel, FuelType.Petrol];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteModelCommand))]
    public partial TransportModel? SelectedModel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransportUnitText))]
    [NotifyCanExecuteChangedFor(nameof(AddItemCommand))]
    public partial SupplyGroup? SelectedGroup { get; set; }

    public ObservableCollection<TransportItemRow> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddItemCommand))]
    public partial TransportModel? AddModel { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddItemCommand))]
    public partial int AddCount { get; set; } = 1;

    [ObservableProperty] public partial string DieselTotal { get; private set; } = string.Empty;
    [ObservableProperty] public partial string PetrolTotal { get; private set; } = string.Empty;

    public string TransportUnitText => SelectedGroup is null
        ? string.Empty
        : Project.FindRow(SelectedGroup.TransportRowId) is { } row
            ? $"Транспорт числится за подразделением подвоза «{row.Row.Name}». При загрузке запасов топливо передаётся туда со склада «{Project.FindRow(SelectedGroup.WarehouseRowId)?.Row.Name ?? "не назначен"}»."
            : "У части не назначено подразделение подвоза (БМТО/РМТО) — назначьте его на вкладке «Структура и нормы».";

    partial void OnSelectedGroupChanged(SupplyGroup? value) => RebuildItems();

    private void RebuildItems()
    {
        Items.Clear();
        if (SelectedGroup is not null)
            foreach (var item in SelectedGroup.Transport)
                if (Project.FindTransport(item.ModelId) is { } model)
                    Items.Add(new TransportItemRow(item, model));
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        foreach (var row in Items)
            row.Refresh();
        if (SelectedGroup is null)
        {
            DieselTotal = PetrolTotal = string.Empty;
            return;
        }
        string Total(FuelType fuel) =>
            $"{SupplyCalculator.TransportCapacityLiters(Project, SelectedGroup, fuel):N0} л = {SupplyCalculator.TransportCapacityKg(Project, SelectedGroup, fuel):N0} кг";
        DieselTotal = Total(FuelType.Diesel);
        PetrolTotal = Total(FuelType.Petrol);
    }

    [RelayCommand]
    private void AddModelToCatalog()
    {
        var model = new TransportModel { Name = "Новая марка", Fuel = FuelType.Diesel, CapacityLiters = 5000 };
        Catalog.Add(model);
        SelectedModel = model;
    }

    private bool HasModel() => SelectedModel is not null;

    [RelayCommand(CanExecute = nameof(HasModel))]
    private void DeleteModel()
    {
        var model = SelectedModel!;
        var used = Groups.Count(g => g.Transport.Any(t => t.ModelId == model.Id));
        if (!_dialogs.Confirm(used > 0
                ? $"Марка «{model.Name}» есть в наличии у частей ({used}). Удалить её из справочника и из наличия частей?"
                : $"Удалить марку «{model.Name}»?"))
            return;
        foreach (var group in Groups)
            foreach (var item in group.Transport.Where(t => t.ModelId == model.Id).ToList())
                group.Transport.Remove(item);
        Catalog.Remove(model);
        RebuildItems();
    }

    private bool CanAddItem() => SelectedGroup is not null && AddModel is not null && AddCount > 0;

    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private void AddItem()
    {
        var existing = SelectedGroup!.Transport.FirstOrDefault(t => t.ModelId == AddModel!.Id);
        if (existing is not null)
            existing.Count += AddCount;
        else
            SelectedGroup.Transport.Add(new TransportItem { ModelId = AddModel!.Id, Count = AddCount });
        AddModel = null;
        AddCount = 1;
        RebuildItems();
    }

    [RelayCommand]
    private void RemoveItem(TransportItemRow? row)
    {
        if (row is null || SelectedGroup is null)
            return;
        SelectedGroup.Transport.Remove(row.Item);
        RebuildItems();
    }
}

public sealed partial class TransportItemRow(TransportItem item, TransportModel model) : ObservableObject
{
    public TransportItem Item { get; } = item;
    public TransportModel Model { get; } = model;

    public int Count
    {
        get => Item.Count;
        set
        {
            if (value < 0 || value == Item.Count)
                return;
            Item.Count = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalLiters));
        }
    }

    public double TotalLiters => Item.Count * Model.CapacityLiters;

    public void Refresh()
    {
        OnPropertyChanged(nameof(TotalLiters));
        OnPropertyChanged(nameof(Model));
    }
}
