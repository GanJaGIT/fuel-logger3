#:project ../src/FuelLog.Supply.Core/FuelLog.Supply.Core.csproj
#:property PublishAot=false

// Создаёт пример для «Потребности и автономности»: проект FuelLog (модель 1) и связанный документ модели 2.
//   dotnet run tools/make-demo-supply.cs -- <папка>
using FuelLog.Core.Model;
using FuelLog.Core.Storage;
using FuelLog.Supply.Core.Calculation;
using FuelLog.Supply.Core.Linking;
using FuelLog.Supply.Core.Model;
using FuelLog.Supply.Core.Storage;

var folder = args.Length > 0 ? args[0] : "samples/supply";
Directory.CreateDirectory(folder);
var model1Path = Path.GetFullPath(Path.Combine(folder, "example-model1.gsmproject"));
var supplyPath = Path.GetFullPath(Path.Combine(folder, "example.gsmsupply"));
var d1 = new DateOnly(2026, 3, 10);

// ---------- Модель 1: техника и задачи ----------
var m1 = new Project { Name = "Пример рд (модель 1)" };
VehicleModel V(string name, FuelType fuel, double main, double extra, double per100, double perHour)
{
    var m = new VehicleModel { Name = name, Fuel = fuel, MainTankLiters = main, ExtraTankLiters = extra, LitersPer100Km = per100, LitersPerHour = perHour };
    m1.Catalog.Add(m);
    return m;
}
var kamaz = V("КамАЗ-4310", FuelType.Diesel, 250, 125, 40, 5);
var ural = V("Урал-4320", FuelType.Diesel, 300, 60, 42, 6);
var maz = V("МАЗ-543", FuelType.Diesel, 850, 0, 125, 18);
var btr = V("БТР-80", FuelType.Diesel, 290, 0, 50, 12);
var gaz = V("ГАЗ-66", FuelType.Petrol, 210, 0, 32, 4);
var uaz = V("УАЗ-469", FuelType.Petrol, 78, 0, 16, 2);
var uaz452 = V("УАЗ-452", FuelType.Petrol, 56, 0, 18, 2);

var units = new Dictionary<string, Unit>();
Group G(string name, params (string Unit, (VehicleModel M, int N)[] Vehicles)[] list)
{
    var g = new Group { Name = name };
    foreach (var (unitName, vehicles) in list)
    {
        var u = new Unit { Name = unitName };
        foreach (var (m, n) in vehicles)
            u.Vehicles.Add(new UnitVehicle { ModelId = m.Id, Count = n });
        g.Units.Add(u);
        units[$"{name}/{unitName}"] = u;
    }
    m1.Groups.Add(g);
    return g;
}

G("управление",
    ("БМТО", [(kamaz, 10), (ural, 8), (uaz, 3)]),
    ("ББО", [(ural, 4), (uaz, 1)]),
    ("БОР", [(kamaz, 3), (uaz, 1)]),
    ("БСБУС", [(gaz, 4), (kamaz, 2)]),
    ("ПКП", [(btr, 2), (uaz, 2)]),
    ("СФПС", [(kamaz, 2)]),
    ("ПК", [(gaz, 2)]),
    ("ОМедБ", [(uaz452, 4), (kamaz, 2)]));
G("1 рп",
    ("1 рдн", [(maz, 6), (kamaz, 4), (uaz, 2)]),
    ("2 рдн", [(maz, 6), (kamaz, 4), (uaz, 2)]),
    ("3 рдн", [(maz, 6), (kamaz, 4), (uaz, 2)]),
    ("ДБО", [(btr, 4), (kamaz, 2)]),
    ("ДБУ", [(kamaz, 3), (uaz, 2)]),
    ("РМТО", [(kamaz, 6), (ural, 4)]),
    ("ГИО", [(ural, 3)]),
    ("ЭРО", [(kamaz, 2), (gaz, 2)]),
    ("ПК", [(gaz, 2)]),
    ("МедП", [(uaz452, 2)]));
G("трб",
    ("рмто", [(kamaz, 4), (ural, 2)]),
    ("1ГИР ПУ", [(maz, 2), (kamaz, 2)]),
    ("2ГИР ПУ", [(maz, 2), (kamaz, 2)]));

void Task(string name, DateOnly date, params (string Unit, VehicleModel M, int N, double Km, double H)[] items)
{
    var t = new FuelTask { Name = name, TaskType = name, Date = date };
    foreach (var (unit, m, n, km, h) in items)
        t.Items.Add(new TaskItem { UnitId = units[unit].Id, ModelId = m.Id, Count = n, DistanceKm = km, WorkHours = h });
    m1.Tasks.Add(t);
}

for (var i = 0; i < 3; i++)
{
    var date = d1.AddDays(i);
    var march = i == 0 ? 120 : 40;
    Task("Совершение марша", date,
        ("1 рп/1 рдн", maz, 6, march, 0), ("1 рп/1 рдн", uaz, 2, march, 0),
        ("1 рп/2 рдн", maz, 6, march, 0), ("1 рп/3 рдн", maz, 6, march / 2, 0));
    Task("Несение дежурства на ПП", date,
        ("1 рп/1 рдн", maz, 6, 0, 8), ("1 рп/2 рдн", maz, 6, 0, 8), ("1 рп/3 рдн", maz, 6, 0, 6),
        ("трб/1ГИР ПУ", maz, 2, 0, 6), ("трб/2ГИР ПУ", maz, 2, 0, 6));
    Task("Подвоз ГСМ на ПП", date,
        ("1 рп/РМТО", kamaz, 6, 80, 1), ("управление/БМТО", kamaz, 10, 100, 1), ("трб/рмто", kamaz, 4, 60, 1));
    Task("Разведка ПП", date, ("управление/БОР", uaz, 1, 90, 2), ("1 рп/ДБО", btr, 4, 50, 4));
    Task("Управление и связь", date, ("управление/ПКП", btr, 2, 0, 10), ("управление/БСБУС", gaz, 4, 20, 6), ("1 рп/ЭРО", gaz, 2, 30, 4));
}
ProjectStore.Save(m1, model1Path);

// ---------- Модель 2: структура, транспорт, наличие, сутки ----------
var supply = new SupplyProject { Name = "Пример рд" };
foreach (var model in StructureBuilder.DefaultTransportCatalog())
    supply.TransportCatalog.Add(model);
supply.Links.Add(new LinkedProject { Path = Model1Context.StoredPath(model1Path, supplyPath) });
var context = Model1Context.Load(supply, supplyPath);
StructureBuilder.Sync(supply, context);

SupplyGroup Group(string name) => supply.Groups.Single(g => StructureBuilder.Normalize(g.Name) == StructureBuilder.Normalize(name));
SupplyRow Row(string group, string name) => Group(group).Rows.Single(r => StructureBuilder.Normalize(r.Name) == StructureBuilder.Normalize(name));
TransportModel T(string prefix) => supply.TransportCatalog.First(t => t.Name.StartsWith(prefix, StringComparison.Ordinal));

// Наличие транспорта подвоза — как на образце.
void Transport(string group, params (string Model, int Count)[] items)
{
    foreach (var (model, count) in items)
        Group(group).Transport.Add(new TransportItem { ModelId = T(model).Id, Count = count });
}
Transport("управление", ("АТЗ-7-5350", 7), ("АЦ-7-4310", 1), ("АЦ-5,5-4310", 7), ("ТЗА-7,5-5335", 2), ("АЦ-8,7-5320", 1),
    ("АЦ-10-4320", 1), ("АЦ-9-5320", 7), ("АТЗ-9,0-4320", 1), ("АТМЗ-5,5-4310", 1));
Transport("1 рп", ("АТЗ-10-4320", 1), ("АЦ-10-4320", 1), ("АЦ-9-5320", 1), ("АТЗ-8-4320", 1), ("АЦ-7-4310", 1),
    ("АТЗ-7-5350", 2), ("АМЗ-7,0-5557", 1), ("АТМЗ-5,5-4310", 1));
Transport("трб", ("АЦ-10-4320", 2), ("АТМЗ-5,5-4310", 1));

// Наличие на складах — около 60 % установленных запасов (как будто по книгам учёта).
var day1 = DayOperations.AddDay(supply, d1);
foreach (var fuel in new[] { FuelType.Diesel, FuelType.Petrol })
{
    var first = SupplyCalculator.Compute(supply, context, fuel)[0];
    foreach (var line in first.AllRows.Where(r => r.Row.Kind == RowKind.Warehouse))
    {
        var kg = Math.Round(line.Figures.NormKg * 0.6 / 100) * 100;
        if (line.Row.Name == "Склад рп")
            kg = fuel == FuelType.Diesel ? 60000 : 3000;
        line.Row.SetStartKg(fuel, kg);
    }
}

// Сутки 1: подвоз от округа на склад рд и со склада рд на склад 1 рп, загрузка транспорта 1 рп, дозаправка рдн, потери.
day1.RowEntry(Row("управление", "Склад рд").Id, FuelType.Diesel).ReceivedKg = 30000;
day1.RowEntry(Row("1 рп", "Склад рп").Id, FuelType.Diesel).ReceivedKg = 20000;
day1.GroupEntry(Group("1 рп").Id, FuelType.Diesel).InTransport = true;
foreach (var name in new[] { "1рдн", "2рдн", "3рдн" })
    day1.RowEntry(Row("1 рп", name).Id, FuelType.Diesel).Refuel = true;
day1.RowEntry(Row("управление", "ПКП").Id, FuelType.Diesel).LossKg = 120;

// Сутки 2: дозаправка всей техники 1 рп и трб, подвоз на склад трб.
var day2 = DayOperations.AddDay(supply, d1.AddDays(1));
foreach (var row in Group("1 рп").Rows.Concat(Group("трб").Rows).Where(r => r.Kind == RowKind.Unit))
{
    day2.RowEntry(row.Id, FuelType.Diesel).Refuel = true;
    day2.RowEntry(row.Id, FuelType.Petrol).Refuel = true;
}
day2.RowEntry(Row("трб", "Склад трб").Id, FuelType.Diesel).ReceivedKg = 8000;

// Сутки 3: топливо из транспорта 1 рп возвращено на склад, дозаправка управления.
var day3 = DayOperations.AddDay(supply, d1.AddDays(2));
day3.GroupEntry(Group("1 рп").Id, FuelType.Diesel).InTransport = false;
foreach (var row in Group("управление").Rows.Where(r => r.Kind == RowKind.Unit))
    day3.RowEntry(row.Id, FuelType.Diesel).Refuel = true;

SupplyStore.Save(supply, supplyPath);
foreach (var backup in new[] { ProjectStore.BackupFolder(model1Path), SupplyStore.BackupFolder(supplyPath) })
    if (Directory.Exists(backup))
        Directory.Delete(backup, true);

foreach (var fuel in new[] { FuelType.Diesel, FuelType.Petrol })
{
    var results = SupplyCalculator.Compute(supply, Model1Context.Load(supply, supplyPath), fuel);
    foreach (var r in results)
        Console.WriteLine($"{fuel.ShortName()} {r.Day.Date:dd.MM}: З {r.Total.NormKg:N0}, Н {r.Total.BalanceKg:N0}, П {r.Total.NeedKg:N0}, А {r.Total.AutonomyDays:N1}; предупреждений: {r.Warnings.Count}");
}
Console.WriteLine($"Готово: {supplyPath}");
