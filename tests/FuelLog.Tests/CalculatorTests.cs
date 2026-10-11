using FuelLog.Core.Calculation;
using FuelLog.Core.Model;

namespace FuelLog.Tests;

public class CalculatorTests
{
    private static readonly VehicleModel Ural = new()
    {
        Name = "Урал-4320",
        Fuel = FuelType.Diesel,
        MainTankLiters = 300,
        ExtraTankLiters = 60,
        LitersPer100Km = 42,
        LitersPerHour = 6,
    };

    [Theory]
    [InlineData(10, 50, 5)]
    [InlineData(5, 20, 1)]
    [InlineData(3, 100, 3)]
    [InlineData(3, 50, 2)] // 1,5 → 2
    [InlineData(4, 10, 0)] // 0,4 → 0
    [InlineData(4, 150, 4)]
    [InlineData(0, 50, 0)]
    [InlineData(7, -5, 0)]
    public void Lost_count_follows_percentage(int count, double percent, int expected) =>
        Assert.Equal(expected, FuelCalculator.LostCount(count, percent));

    [Fact]
    public void Working_vehicles_burn_march_and_work_fuel()
    {
        var settings = new ProjectSettings { DieselDensity = 0.84 };
        var item = new TaskItem { Count = 2, DistanceKm = 150, WorkHours = 4 };

        var calc = FuelCalculator.Calculate(item, Ural, settings);

        Assert.Equal(2 * 1.5 * 42, calc.MarchLiters, 9);
        Assert.Equal(2 * 4 * 6, calc.WorkLiters, 9);
        Assert.Equal(0, calc.LossLiters);
        Assert.Equal((126 + 48) * 0.84, calc.TotalKg, 9);
    }

    [Fact]
    public void Lost_vehicles_write_off_full_tanks_and_do_not_work()
    {
        var settings = new ProjectSettings();
        var item = new TaskItem { Count = 10, DistanceKm = 100, WorkHours = 0, LossPercent = 30 };

        var calc = FuelCalculator.Calculate(item, Ural, settings);

        Assert.Equal(3, calc.LostCount);
        Assert.Equal(7, calc.WorkingCount);
        Assert.Equal(7 * 42, calc.MarchLiters, 9);
        Assert.Equal(3 * 360, calc.LossLiters, 9);
    }

    [Fact]
    public void Refill_mass_uses_density_per_fuel_and_ignores_unknown_models()
    {
        var project = new Project();
        project.Settings.PetrolDensity = 0.75;
        project.Settings.DieselDensity = 0.85;
        var gaz = new VehicleModel { Name = "ГАЗ-66", Fuel = FuelType.Petrol, MainTankLiters = 210 };
        project.Catalog.Add(gaz);
        project.Catalog.Add(Ural);
        var unit = new Unit();
        unit.Vehicles.Add(new UnitVehicle { ModelId = gaz.Id, Count = 2 });
        unit.Vehicles.Add(new UnitVehicle { ModelId = Ural.Id, Count = 3 });
        unit.Vehicles.Add(new UnitVehicle { ModelId = Guid.NewGuid(), Count = 99 });

        var mass = FuelCalculator.RefillMass(unit, project);

        Assert.Equal(2 * 210 * 0.75, mass.Petrol, 9);
        Assert.Equal(3 * 360 * 0.85, mass.Diesel, 9);
    }

    [Fact]
    public void Fuel_pair_division_by_zero_gives_zero()
    {
        var result = new FuelPair(10, 5) / new FuelPair(0, 10);
        Assert.Equal(new FuelPair(0, 0.5), result);
        Assert.Equal(FuelPair.Zero, new FuelPair(1, 1) / 0);
    }
}
