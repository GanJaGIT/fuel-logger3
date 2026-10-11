using ClosedXML.Excel;
using FuelLog.Core.Model;
using FuelLog.Core.Storage;
using FuelLog.Supply.Core.Calculation;
using FuelLog.Supply.Core.Linking;
using FuelLog.Supply.Core.Model;
using FuelLog.Supply.Core.Reporting;
using FuelLog.Supply.Core.Storage;

namespace FuelLog.Supply.Tests;

/// <summary>
/// Модель 1: плотность 1 кг/л, марка «Т» (ДТ, бак 100 л, 10 л/ч).
/// управление: БМТО — 2 ед. (Мз 200), ББО — 1 ед. (Мз 100); 1 рп: 1 рдн — 5 ед. (Мз 500), РМТО — 1 ед. (Мз 100).
/// Мз рд = 900, Мз 1рп = 600. Задачи 10.03: 1 рдн — 5 ед. × 2 ч (100 кг), БМТО — 2 ед. × 1 ч (20 кг).
/// </summary>
public sealed class SupplyTests : IDisposable
{
    private static readonly DateOnly Day1 = new(2026, 3, 10);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fuellog-supply-tests", Guid.NewGuid().ToString("N"));
    private readonly string _supplyPath;
    private readonly SupplyProject _project;
    private Model1Context _model1;

    public SupplyTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "модель1"));
        var model1Path = Path.Combine(_dir, "модель1", "рд.gsmproject");
        ProjectStore.Save(CreateModel1(), model1Path);

        _supplyPath = Path.Combine(_dir, "рд.gsmsupply");
        _project = StructureBuilder.CreateTypical("рд", regiments: 1, technicalBase: false);
        _project.Settings.DieselDensity = 0.85;
        _project.Links.Add(new LinkedProject { Path = Model1Context.StoredPath(model1Path, _supplyPath) });
        _model1 = Model1Context.Load(_project, _supplyPath);
        StructureBuilder.Sync(_project, _model1);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Project CreateModel1()
    {
        var project = new Project { Name = "Модель 1" };
        project.Settings.PetrolDensity = 1;
        project.Settings.DieselDensity = 1;
        var model = new VehicleModel { Name = "Т", Fuel = FuelType.Diesel, MainTankLiters = 100, LitersPerHour = 10 };
        project.Catalog.Add(model);

        Unit U(Group g, string name, int count)
        {
            var unit = new Unit { Name = name };
            unit.Vehicles.Add(new UnitVehicle { ModelId = model.Id, Count = count });
            g.Units.Add(unit);
            return unit;
        }

        var hq = new Group { Name = "Управление" };
        var rp = new Group { Name = "1 рп" };
        project.Groups.Add(hq);
        project.Groups.Add(rp);
        var bmto = U(hq, "БМТО", 2);
        U(hq, "ББО", 1);
        var rdn = U(rp, "1 рдн", 5);
        U(rp, "РМТО", 1);

        var task = new FuelTask { Name = "Задача", Date = Day1 };
        task.Items.Add(new TaskItem { UnitId = rdn.Id, ModelId = model.Id, Count = 5, WorkHours = 2 });
        task.Items.Add(new TaskItem { UnitId = bmto.Id, ModelId = model.Id, Count = 2, WorkHours = 1 });
        project.Tasks.Add(task);
        return project;
    }

    private SupplyRow Row(string group, string name) =>
        _project.Groups.Single(g => StructureBuilder.Normalize(g.Name) == StructureBuilder.Normalize(group))
            .Rows.Single(r => StructureBuilder.Normalize(r.Name) == StructureBuilder.Normalize(name));

    private SupplyGroup Group(string name) =>
        _project.Groups.Single(g => StructureBuilder.Normalize(g.Name) == StructureBuilder.Normalize(name));

    private IReadOnlyList<DayResult> Diesel() => SupplyCalculator.Compute(_project, _model1, FuelType.Diesel);

    [Fact]
    public void Sync_links_template_rows_to_model1_units_by_name()
    {
        Assert.True(Row("управление", "БМТО").IsLinked);
        Assert.True(Row("управление", "ББО").IsLinked);
        Assert.True(Row("1рп", "1рдн").IsLinked);
        Assert.True(Row("1рп", "РМТО").IsLinked);
        Assert.False(Row("1рп", "2рдн").IsLinked);
        Assert.False(Row("1рп", "Склад рп").IsLinked);

        // Повторное сопоставление ничего не меняет.
        Assert.True(StructureBuilder.Sync(_project, _model1).IsEmpty);
        Assert.Equal(Row("управление", "Склад рд").Id, _project.MainWarehouseRowId);
    }

    [Fact]
    public void Established_reserves_follow_order_074_exceptions()
    {
        DayOperations.AddDay(_project, Day1);
        var day = Diesel().Single();
        double Norm(string g, string r) => day.Find(Row(g, r).Id)!.Figures.NormKg;

        Assert.Equal(3.1 * 900, Norm("управление", "Склад рд"), 6);      // 3,1 × Мз рд
        Assert.Equal(1 * 200 + 0.4 * 900, Norm("управление", "БМТО"), 6); // 1 × Мз БМТО + 0,4 × Мз рд
        Assert.Equal(100, Norm("управление", "ББО"), 6);
        Assert.Equal(2.3 * 600, Norm("1рп", "Склад рд"), 6);             // 2,3 × Мз рп
        Assert.Equal(1.4 * 500, Norm("1рп", "1рдн"), 6);
        Assert.Equal(1 * 100 + 0.4 * 600, Norm("1рп", "РМТО"), 6);       // 1 × Мз РМТО + 0,4 × Мз рп
        Assert.Equal(1.4, day.Find(Row("управление", "БМТО").Id)!.NormRefills, 6);
        Assert.Equal(400, day.Find(Row("управление", "БМТО").Id)!.Figures.RefillMass, 6); // 560 кг / 1,4 запр
    }

    [Fact]
    public void Day_balance_with_refuel_delivery_loss_and_transport()
    {
        Row("управление", "Склад рд").StartDieselKg = 5000;
        Row("1рп", "Склад рп").StartDieselKg = 3000;
        var rp = Group("1рп");
        var tanker = _project.TransportCatalog.First(t => t.Fuel == FuelType.Diesel);
        tanker.CapacityLiters = 1000;
        rp.Transport.Add(new TransportItem { ModelId = tanker.Id, Count = 2 }); // 2000 л × 0,85 = 1700 кг

        var day = DayOperations.AddDay(_project, Day1);
        day.RowEntry(Row("1рп", "1рдн").Id, FuelType.Diesel).Refuel = true;
        day.RowEntry(Row("1рп", "Склад рп").Id, FuelType.Diesel).ReceivedKg = 200;
        day.RowEntry(Row("управление", "ББО").Id, FuelType.Diesel).LossKg = 10;
        day.GroupEntry(rp.Id, FuelType.Diesel).InTransport = true;

        var result = Diesel().Single();
        RowResult R(string g, string r) => result.Find(Row(g, r).Id)!;

        // 1 рдн: начало — 1 заправка (500), расход по модели 1 — 100, дозаправка до З = 700 → получено 300.
        var rdn = R("1рп", "1рдн");
        Assert.Equal(500, rdn.Figures.OpeningKg, 6);
        Assert.Equal(100, rdn.Expense.Tasks, 6);
        Assert.Equal(300, rdn.Income.Refuel, 6);
        Assert.Equal(700, rdn.Figures.BalanceKg, 6);
        Assert.Equal(0, rdn.Figures.NeedKg, 6);
        Assert.Equal(7, rdn.Figures.AutonomyDays!.Value, 6); // 700 кг / 100 кг/сут
        Assert.Equal(1.4, rdn.Figures.Refills(rdn.Figures.BalanceKg)!.Value, 6);

        // Склад рп: 3000 + подвоз 200 − дозаправка 300 − загрузка в транспорт 1700 = 1200.
        var warehouse = R("1рп", "Склад рп");
        Assert.Equal(200, warehouse.Income.Delivery, 6);
        Assert.Equal(300, warehouse.Expense.RefuelIssued, 6);
        Assert.Equal(1700, warehouse.Expense.TransportIssued, 6);
        Assert.Equal(1200, warehouse.Figures.BalanceKg, 6);
        Assert.Equal(12, warehouse.Figures.AutonomyDays!.Value, 6); // обеспечивает 1рп: 100 кг/сут

        // РМТО: начало — 1 заправка строки ((100 + 0,4 × 600) / 1,4), принимает топливо в транспорт.
        Assert.Equal(340 / 1.4 + 1700, R("1рп", "РМТО").Figures.BalanceKg, 6);

        // Склад рд выдал на склад рп всё, что тот получил при подвозе.
        Assert.Equal(200, R("управление", "Склад рд").Expense.DeliveryIssued, 6);
        Assert.Equal(4800, R("управление", "Склад рд").Figures.BalanceKg, 6);

        // ББО: потери 10.
        Assert.Equal(90, R("управление", "ББО").Figures.BalanceKg, 6);
        Assert.Empty(result.Warnings);

        // Итог части — сумма строк.
        var group = result.Groups.Single(g => g.Group == rp);
        Assert.Equal(group.Rows.Sum(r => r.Figures.BalanceKg), group.Totals.BalanceKg, 6);
    }

    [Fact]
    public void Next_day_starts_from_previous_balance_and_keeps_transport_state()
    {
        Row("1рп", "Склад рп").StartDieselKg = 3000;
        var rp = Group("1рп");
        var tanker = _project.TransportCatalog.First(t => t.Fuel == FuelType.Diesel);
        tanker.CapacityLiters = 1000;
        rp.Transport.Add(new TransportItem { ModelId = tanker.Id, Count = 1 }); // 850 кг

        var day1 = DayOperations.AddDay(_project, Day1);
        day1.GroupEntry(rp.Id, FuelType.Diesel).InTransport = true;
        var day2 = DayOperations.AddDay(_project, DayOperations.NextDate(_project, Day1));
        Assert.Equal(Day1.AddDays(1), day2.Date);
        Assert.True(day2.FindGroup(rp.Id, FuelType.Diesel)!.InTransport);

        var day3 = DayOperations.AddDay(_project, DayOperations.NextDate(_project, Day1));
        day3.GroupEntry(rp.Id, FuelType.Diesel).InTransport = false; // разгрузили

        var results = Diesel();
        var warehouseId = Row("1рп", "Склад рп").Id;
        Assert.Equal(3000 - 850, results[0].Find(warehouseId)!.Figures.BalanceKg, 6);
        Assert.Equal(results[0].Find(warehouseId)!.Figures.BalanceKg, results[1].Find(warehouseId)!.Figures.OpeningKg, 6);
        Assert.Equal(0, results[1].Find(warehouseId)!.Figures.ExpenseKg, 6); // состояние не менялось
        Assert.Equal(3000, results[2].Find(warehouseId)!.Figures.BalanceKg, 6); // топливо вернулось на склад

        // На вторые сутки задач в модели 1 нет — расход подразделений нулевой.
        Assert.Equal(0, results[1].Find(Row("1рп", "1рдн").Id)!.Expense.Tasks);

        Assert.False(DayOperations.CanRemove(_project, day1));
        Assert.True(DayOperations.CanRemove(_project, day3));
        Assert.Throws<InvalidOperationException>(() => DayOperations.AddDay(_project, Day1));
    }

    [Fact]
    public void Refuel_amount_can_be_set_manually_and_negative_warehouse_is_reported()
    {
        var day = DayOperations.AddDay(_project, Day1);
        var entry = day.RowEntry(Row("1рп", "1рдн").Id, FuelType.Diesel);
        entry.Refuel = true;
        entry.RefuelKg = 50;

        var result = Diesel().Single();
        Assert.Equal(50, result.Find(Row("1рп", "1рдн").Id)!.Income.Refuel, 6);
        Assert.Equal(-50, result.Find(Row("1рп", "Склад рп").Id)!.Figures.BalanceKg, 6);
        Assert.Contains(result.Warnings, w => w.Contains("Склад рп") && w.Contains("отрицательное"));
    }

    [Fact]
    public void Model1_changes_are_picked_up_on_refresh()
    {
        DayOperations.AddDay(_project, Day1);
        var model1Path = Model1Context.ResolvePath(_project.Links[0], _supplyPath);
        Assert.False(_model1.IsStale(_project, _supplyPath));

        var model1 = ProjectStore.Load(model1Path).Project;
        model1.Settings.DieselDensity = 2;
        ProjectStore.Save(model1, model1Path);
        File.SetLastWriteTimeUtc(model1Path, DateTime.UtcNow.AddMinutes(1));

        Assert.True(_model1.IsStale(_project, _supplyPath));
        _model1 = _model1.Refresh(_project, _supplyPath);
        Assert.Equal(2 * 100, Diesel().Single().Find(Row("управление", "ББО").Id)!.Figures.NormKg, 6);
    }

    [Fact]
    public void Missing_model1_file_is_reported()
    {
        File.Delete(Model1Context.ResolvePath(_project.Links[0], _supplyPath));
        DayOperations.AddDay(_project, Day1);
        _model1 = Model1Context.Load(_project, _supplyPath);

        var result = Diesel().Single();
        Assert.False(_model1.Models[0].IsLoaded);
        Assert.Contains(result.Warnings, w => w.Contains("недоступен"));
    }

    [Fact]
    public void Project_round_trips_with_relative_link()
    {
        var day = DayOperations.AddDay(_project, Day1);
        day.RowEntry(Row("управление", "ББО").Id, FuelType.Petrol).LossKg = 3;
        SupplyStore.Save(_project, _supplyPath);

        var loaded = SupplyStore.Load(_supplyPath);
        Assert.Equal(Path.Combine("модель1", "рд.gsmproject"), loaded.Links[0].Path);
        Assert.Equal(_project.AllRows.Count(), loaded.AllRows.Count());
        Assert.Equal(3, loaded.Days[0].FindRow(Row("управление", "ББО").Id, FuelType.Petrol)!.LossKg);
        Assert.Equal(_project.MainWarehouseRowId, loaded.MainWarehouseRowId);

        // Файл модели 1 нельзя открыть как документ модели 2.
        var model1Path = Model1Context.ResolvePath(loaded.Links[0], _supplyPath);
        Assert.Throws<ProjectFormatException>(() => SupplyStore.Load(model1Path));
    }

    [Fact]
    public void Excel_document_has_fuel_tabs_and_operations()
    {
        var day = DayOperations.AddDay(_project, Day1);
        day.RowEntry(Row("1рп", "1рдн").Id, FuelType.Diesel).Refuel = true;
        var path = Path.Combine(_dir, "doc.xlsx");

        SupplyExcelWriter.Write(_project, Diesel().Single(),
            SupplyCalculator.Compute(_project, _model1, FuelType.Petrol).Single(), path);

        using var wb = new XLWorkbook(path);
        var dt = wb.Worksheet("ДТ");
        Assert.Equal("Потребность и автономность (ДТ)", dt.Cell(1, 1).GetString());
        Assert.Equal("Установленные запасы (З)", dt.Cell(3, 3).GetString());
        Assert.Equal("Автономность (А)", dt.Cell(3, 17).GetString());
        Assert.Equal(17, dt.Cell(5, 17).GetDouble());
        Assert.Equal("управление", dt.Cell(6, 1).GetString());
        Assert.Equal("Склад рд", dt.Cell(6, 2).GetString());
        Assert.Equal(3.1 * 900, dt.Cell(6, 3).GetDouble(), 6);
        Assert.True(wb.Worksheets.Contains("АБ"));
        Assert.Contains(wb.Worksheet("Операции").CellsUsed(), c => c.GetString() == "1рдн");
    }
}
