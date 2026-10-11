#:project ../src/FuelLog.Core/FuelLog.Core.csproj
#:property PublishAot=false

// Создаёт демонстрационный проект: dotnet run tools/make-demo.cs -- <путь.gsmproject>
using FuelLog.Core.Model;
using FuelLog.Core.Storage;

var path = args.Length > 0 ? args[0] : "Пример.gsmproject";
var project = Project.CreateNew("Пример расчёта");
project.Groups.Clear();

VehicleModel Model(string name, FuelType fuel, double main, double extra, double per100, double perHour, string category)
{
    var m = new VehicleModel
    {
        Name = name, Fuel = fuel, MainTankLiters = main, ExtraTankLiters = extra,
        LitersPer100Km = per100, LitersPerHour = perHour, Category = category,
    };
    project.Catalog.Add(m);
    return m;
}

var kamaz = Model("КамАЗ-4310", FuelType.Diesel, 250, 125, 40, 5, "АС");
var ural = Model("Урал-4320", FuelType.Diesel, 300, 60, 42, 6, "АС");
var maz = Model("МАЗ-543", FuelType.Diesel, 850, 0, 125, 18, "РВО");
var zil = Model("ЗИЛ-131", FuelType.Petrol, 170, 170, 49, 6, "ГСМ");
var gaz = Model("ГАЗ-66", FuelType.Petrol, 210, 0, 32, 4, "Связь");
var uaz = Model("УАЗ-469", FuelType.Petrol, 78, 0, 16, 2, "Другое");
var btr = Model("БТР-80", FuelType.Diesel, 290, 0, 50, 12, "ВС");

Unit AddUnit(Group g, string name, params (VehicleModel M, int N)[] vehicles)
{
    var u = new Unit { Name = name };
    foreach (var (m, n) in vehicles)
        u.Vehicles.Add(new UnitVehicle { ModelId = m.Id, Count = n });
    g.Units.Add(u);
    return u;
}

var rp1 = new Group { Name = "1рп" };
var rp2 = new Group { Name = "2рп" };
project.Groups.Add(rp1);
project.Groups.Add(rp2);

var rdn1 = AddUnit(rp1, "1 рдн", (maz, 6), (kamaz, 4), (uaz, 2), (gaz, 3));
var rdn2 = AddUnit(rp1, "2 рдн", (maz, 6), (ural, 4), (uaz, 2), (gaz, 2));
var rmto = AddUnit(rp1, "рмто", (zil, 4), (kamaz, 6), (ural, 3));
AddUnit(rp1, "рохр", (btr, 4), (uaz, 1));
var rdn21 = AddUnit(rp2, "1 рдн", (maz, 6), (kamaz, 4), (uaz, 2));

var day = new DateOnly(2026, 3, 10);

void Task(string name, DateOnly date, params (Unit U, VehicleModel M, int N, double Km, double H, double Loss)[] items)
{
    var t = new FuelTask { Name = name, TaskType = name, Date = date };
    foreach (var (u, m, n, km, h, loss) in items)
        t.Items.Add(new TaskItem { UnitId = u.Id, ModelId = m.Id, Count = n, DistanceKm = km, WorkHours = h, LossPercent = loss });
    project.Tasks.Add(t);
}

Task("Совершение марша", day, (rdn1, maz, 6, 120, 0, 0), (rdn1, uaz, 2, 120, 0, 0), (rdn2, maz, 6, 90, 0, 0), (rdn2, ural, 4, 90, 0, 0));
Task("Разведка ПП", day, (rdn1, uaz, 2, 60, 2, 0), (rdn2, uaz, 2, 45, 2, 0));
Task("Инженерная подготовка ПП", day, (rdn1, kamaz, 4, 10, 6, 0), (rdn2, ural, 4, 10, 6, 0));
Task("Несение дежурства на МБП (ПП)", day, (rdn1, maz, 6, 0, 8, 0), (rdn1, gaz, 3, 0, 12, 0), (rdn2, maz, 6, 0, 8, 0));
Task("Подвоз ГСМ на ПП", day, (rmto, zil, 4, 80, 1, 0), (rmto, kamaz, 6, 80, 1, 0));
Task("Подвоз продовольствия на ПП", day, (rmto, ural, 3, 60, 1, 0));
Task("Совершение марша", day.AddDays(1), (rdn21, maz, 6, 150, 0, 17), (rdn21, kamaz, 4, 150, 0, 25));

ProjectStore.Save(project, path);
Console.WriteLine($"Демо-проект: {Path.GetFullPath(path)}");
