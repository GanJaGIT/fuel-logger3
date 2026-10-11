using System.IO;
using FuelLog.Core.Model;
using FuelLog.Supply.Core.Model;
using FuelLog.Supply.Core.Storage;
using FuelLog.Supply.Services;
using FuelLog.Supply.ViewModels;

namespace FuelLog.App.Tests;

/// <summary>Экраны «Потребности и автономности» на демонстрационном примере (рд: управление, 1 рп, трб).</summary>
public sealed class SupplyViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fuellog-supply-vm", Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new();
    private readonly SupplySession _session;

    public SupplyViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        foreach (var file in Directory.GetFiles(MainViewModel.SamplesFolder, "example*"))
            File.Copy(file, Path.Combine(_dir, Path.GetFileName(file)));
        var path = Path.Combine(_dir, "example.gsmsupply");
        _session = new SupplySession(SupplyStore.Load(path), path);
    }

    public void Dispose()
    {
        _session.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private SupplyProject Project => _session.Project;

    [Fact]
    public void Sample_is_linked_and_has_three_days_without_warnings()
    {
        Assert.True(_session.Model1.Models.Single().IsLoaded);
        Assert.Equal(3, Project.Days.Count);
        Assert.All(_session.Diesel.Concat(_session.Petrol), r => Assert.Empty(r.Warnings));
        Assert.All(Project.AllRows.Where(r => r.Kind == RowKind.Unit), r => Assert.True(r.IsLinked, r.Name));
    }

    [Fact]
    public void Document_rows_follow_structure_and_update_after_operations()
    {
        var vm = new DayViewModel(_session, _dialogs);
        Assert.Same(Project.Days[^1], vm.SelectedDay);
        // Заголовок, строки и «Итого» каждой части + «Итого по рд».
        Assert.Equal(Project.Groups.Sum(g => g.Rows.Count + 2) + 1, vm.DocumentRows.Count);

        // ГИО (1 рп) на третьи сутки не дозаправляется — потери уменьшают наличие.
        var loss = vm.LossRows.First(r => r.Row.Name == "ГИО");
        var before = vm.DocumentRows.First(d => d.Row == loss.Row).BalanceKg!.Value;
        loss.LossKg = 50;
        _session.Recalculate();
        Assert.Equal(before - 50, vm.DocumentRows.First(d => d.Row == loss.Row).BalanceKg!.Value, 6);

        vm.IsPetrol = true;
        Assert.Equal(FuelType.Petrol, vm.Fuel);
        Assert.Equal(0, vm.LossRows.First(r => r.Row == loss.Row).LossKg);
    }

    [Fact]
    public void Refuel_checkbox_moves_fuel_from_group_warehouse()
    {
        var vm = new DayViewModel(_session, _dialogs) { SelectedDay = Project.Days[^1] };
        var row = vm.RefuelRows.First(r => r.Row.Name == "1рдн");
        Assert.False(row.IsChecked);
        Assert.True(row.NeedKg > 0);

        var need = row.NeedKg;
        row.IsChecked = true;
        _session.Recalculate();

        Assert.Equal(need, row.ReceivedKg, 6);
        var doc = vm.DocumentRows.First(d => d.Row == row.Row);
        Assert.Equal(0, doc.NeedKg!.Value, 6);

        row.ManualKg = 100;
        _session.Recalculate();
        Assert.Equal(100, row.ReceivedKg, 6);
    }

    [Fact]
    public void Transport_toggle_and_new_day_keep_state()
    {
        var vm = new DayViewModel(_session, _dialogs);
        var trb = vm.TransportRows.First(r => r.Group.Name == "трб");
        Assert.False(trb.InTransport);
        trb.InTransport = true;
        _session.Recalculate();
        Assert.Contains("загружено", trb.ChangeText);

        vm.AddDayCommand.Execute(null);
        Assert.Equal(4, Project.Days.Count);
        Assert.Equal(new DateOnly(2026, 3, 13), vm.SelectedDay!.Date);
        Assert.True(vm.TransportRows.First(r => r.Group.Name == "трб").InTransport);

        Assert.True(vm.RemoveDayCommand.CanExecute(null));
        vm.RemoveDayCommand.Execute(null);
        Assert.Equal(3, Project.Days.Count);
    }

    [Fact]
    public void Export_writes_excel_into_reports_folder()
    {
        var vm = new DayViewModel(_session, _dialogs);
        vm.ExportCommand.Execute(null);
        var file = Assert.Single(Directory.GetFiles(_session.ReportsFolder, "*.xlsx"));
        Assert.Equal(file, Assert.Single(_dialogs.Opened));
    }

    [Fact]
    public void Structure_editor_changes_norm_and_main_warehouse()
    {
        var vm = new StructureViewModel(_session, _dialogs);
        vm.Refresh();
        var hq = Project.Groups[0];
        var bbo = hq.Rows.First(r => r.Name == "ББО");
        vm.SelectedNode = bbo;
        Assert.Equal("З = 1 × Мз своей техники", vm.NormFormula);
        Assert.NotSame(StructureViewModel.NotLinked, vm.SelectedLink);

        vm.AddTermCommand.Execute(null);
        vm.SelectedTerm!.Coefficient = 0.5;
        _session.Recalculate();
        Assert.Contains("0,5 × Мз своей части", vm.NormFormula);

        vm.SelectedNode = hq.Rows.First(r => r.Name == "Склад рд");
        Assert.True(vm.RowIsMainWarehouse);
        vm.SelectedNode = hq;
        Assert.Equal("Склад рд", vm.GroupWarehouse!.Name);
        Assert.Equal("БМТО", vm.GroupTransport!.Name);
    }

    [Fact]
    public void Links_screen_reports_status_and_unlinks()
    {
        var vm = new LinksViewModel(_session, _dialogs);
        var item = Assert.Single(vm.Items);
        Assert.True(item.IsOk);
        Assert.Contains("подразделений: 21", item.Status);
        Assert.Contains("Все подразделения", vm.UnlinkedRowsText);

        vm.RemoveCommand.Execute(item);
        Assert.Empty(Project.Links);
        Assert.Empty(vm.Items);
        Assert.Contains("Не привязаны", vm.UnlinkedRowsText);
    }

    [Fact]
    public void Start_balances_default_to_one_refill_for_units()
    {
        var vm = new StartBalancesViewModel(_session);
        vm.IsDiesel = true;
        var rdn = vm.Rows.First(r => r.Row.Name == "1рдн");
        Assert.Null(rdn.ManualKg);
        Assert.Equal(1, rdn.StartRefills!.Value, 6);

        rdn.ManualKg = 1000;
        _session.Recalculate();
        Assert.Equal(1000, rdn.StartKg, 6);
        vm.ResetUnitsCommand.Execute(null);
        Assert.Null(rdn.ManualKg);
    }
}
