using FuelLog.Core.Model;

namespace FuelLog.Tests;

/// <summary>
/// Проект, воспроизводящий «Расчёты образец.xlsx».
/// Плотность 1 кг/л, чтобы литры совпадали с килограммами; нормы подобраны под расход из образца.
/// </summary>
internal static class SampleProject
{
    public static readonly DateOnly Date = new(2026, 3, 10);

    public static Project Create()
    {
        var project = new Project { Name = "Образец" };
        project.Settings.PetrolDensity = 1;
        project.Settings.DieselDensity = 1;

        // «Ёмкостные» марки задают массу заправки подразделения, «рабочие» дают расход 1 л/ч.
        var petrolTank = Model(project, "АБ-ёмкость", FuelType.Petrol, tank: 1);
        var dieselTank = Model(project, "ДТ-ёмкость", FuelType.Diesel, tank: 1);
        var petrolWorker = Model(project, "АБ-работа", FuelType.Petrol, tank: 0, perHour: 1);
        var dieselWorker = Model(project, "ДТ-работа", FuelType.Diesel, tank: 0, perHour: 1);

        var rp1 = new Group { Name = "1рп" };
        project.Groups.Add(rp1);
        project.Groups.Add(new Group { Name = "2рп" }); // без задач — в отчёт не попадает

        var rdn1 = Unit(rp1, "1 рдн", petrol: 766, diesel: 18122, petrolTank, dieselTank, petrolWorker, dieselWorker);
        var rdn2 = Unit(rp1, "2 рдн", petrol: 766, diesel: 18122, petrolTank, dieselTank, petrolWorker, dieselWorker);
        var rmto = Unit(rp1, "рмто", petrol: 786, diesel: 6476, petrolTank, dieselTank, petrolWorker, dieselWorker);
        // Подразделение без задач: его техника входит в массу заправки 1рп (2987 и 76829 в образце).
        Unit(rp1, "резерв", petrol: 2987 - 766 - 766 - 786, diesel: 76829 - 18122 - 18122 - 6476, petrolTank, dieselTank, petrolWorker, dieselWorker);

        void AddTask(string name, params (Unit Unit, double PetrolKg, double DieselKg)[] lines)
        {
            var task = new FuelTask { Name = name, TaskType = name, Date = Date };
            foreach (var (unit, petrolKg, dieselKg) in lines)
            {
                if (petrolKg > 0)
                    task.Items.Add(new TaskItem { UnitId = unit.Id, ModelId = petrolWorker.Id, Count = 1, WorkHours = petrolKg });
                if (dieselKg > 0)
                    task.Items.Add(new TaskItem { UnitId = unit.Id, ModelId = dieselWorker.Id, Count = 1, WorkHours = dieselKg });
            }
            project.Tasks.Add(task);
        }

        AddTask("Инженерная подготовка ПП", (rdn1, 91.26, 1812.2), (rdn2, 0, 2350));
        AddTask("Разведка ПП", (rdn1, 91.92, 1812.2), (rdn2, 0, 550));
        AddTask("Совершение марша", (rdn1, 76.6, 906.1), (rdn2, 500, 2500));
        AddTask("Несение дежурства на МБП (ПП)", (rdn1, 0, 2781.3), (rdn2, 0, 1200));
        AddTask("Подвоз ГСМ на ПП", (rmto, 100, 400));
        AddTask("Дозапрвка агрегатов БРК", (rmto, 0, 50));
        AddTask("Подвоз продовольствия на ПП", (rmto, 500, 120));

        return project;
    }

    private static VehicleModel Model(Project project, string name, FuelType fuel, double tank, double perHour = 0)
    {
        var model = new VehicleModel { Name = name, Fuel = fuel, MainTankLiters = tank, LitersPerHour = perHour };
        project.Catalog.Add(model);
        return model;
    }

    private static Unit Unit(Group group, string name, double petrol, double diesel, params VehicleModel[] models)
    {
        var unit = new Unit { Name = name };
        unit.Vehicles.Add(new UnitVehicle { ModelId = models[0].Id, Count = (int)petrol });
        unit.Vehicles.Add(new UnitVehicle { ModelId = models[1].Id, Count = (int)diesel });
        unit.Vehicles.Add(new UnitVehicle { ModelId = models[2].Id, Count = 1 });
        unit.Vehicles.Add(new UnitVehicle { ModelId = models[3].Id, Count = 1 });
        group.Units.Add(unit);
        return unit;
    }
}
