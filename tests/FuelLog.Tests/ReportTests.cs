using FuelLog.Core.Calculation;
using FuelLog.Core.Model;

namespace FuelLog.Tests;

public class ReportTests
{
    private static void AssertFigures(ReportFigures f, double abKg, double abRefills, double dtKg, double dtRefills, double abMass, double dtMass)
    {
        Assert.Equal(abKg, Math.Round(f.Kg.Petrol, 2));
        Assert.Equal(abRefills, Math.Round(f.Refills.Petrol, 2));
        Assert.Equal(dtKg, Math.Round(f.Kg.Diesel, 2));
        Assert.Equal(dtRefills, Math.Round(f.Refills.Diesel, 2));
        Assert.Equal(abMass, Math.Round(f.Mass.Petrol));
        Assert.Equal(dtMass, Math.Round(f.Mass.Diesel));
        // Все задачи в один день — среднесуточный расход равен расходу в заправках, как в образце.
        Assert.Equal(Math.Round(f.Refills.Petrol, 2), Math.Round(f.DailyRefills.Petrol, 2));
        Assert.Equal(Math.Round(f.Refills.Diesel, 2), Math.Round(f.DailyRefills.Diesel, 2));
    }

    [Fact]
    public void Reproduces_sample_rows_and_refills()
    {
        var report = FuelReport.Build(SampleProject.Create());

        var group = Assert.Single(report.Groups);
        Assert.Equal("1рп", group.Group.Name);
        Assert.Equal(["1 рдн", "2 рдн", "рмто"], group.Units.Select(u => u.Unit.Name));

        var rdn1 = group.Units[0];
        Assert.Equal(
            ["Инженерная подготовка ПП", "Разведка ПП", "Совершение марша", "Несение дежурства на МБП (ПП)"],
            rdn1.Rows.Select(r => r.Task.Name));
        AssertFigures(rdn1.Rows[0].Figures, 91.26, 0.12, 1812.2, 0.10, 766, 18122);
        AssertFigures(rdn1.Rows[1].Figures, 91.92, 0.12, 1812.2, 0.10, 766, 18122);
        AssertFigures(rdn1.Rows[2].Figures, 76.6, 0.10, 906.1, 0.05, 766, 18122);
        AssertFigures(rdn1.Rows[3].Figures, 0, 0, 2781.3, 0.15, 766, 18122);
        // В образце итог набран вручную (260,44 и 7248,8), арифметическая сумма строк — 259,78 и 7311,8.
        AssertFigures(rdn1.Figures, 259.78, 0.34, 7311.8, 0.40, 766, 18122);

        var rdn2 = group.Units[1];
        AssertFigures(rdn2.Rows[0].Figures, 0, 0, 2350, 0.13, 766, 18122);
        AssertFigures(rdn2.Rows[1].Figures, 0, 0, 550, 0.03, 766, 18122);
        AssertFigures(rdn2.Rows[2].Figures, 500, 0.65, 2500, 0.14, 766, 18122);
        AssertFigures(rdn2.Rows[3].Figures, 0, 0, 1200, 0.07, 766, 18122);
        AssertFigures(rdn2.Figures, 500, 0.65, 6600, 0.36, 766, 18122);

        var rmto = group.Units[2];
        AssertFigures(rmto.Rows[0].Figures, 100, 0.13, 400, 0.06, 786, 6476);
        AssertFigures(rmto.Rows[1].Figures, 0, 0, 50, 0.01, 786, 6476);
        AssertFigures(rmto.Rows[2].Figures, 500, 0.64, 120, 0.02, 786, 6476);
        AssertFigures(rmto.Figures, 600, 0.76, 570, 0.09, 786, 6476);

        // Масса заправки 1рп — по всей технике части, включая подразделения без задач.
        AssertFigures(group.Figures, 1359.78, 0.46, 14481.8, 0.19, 2987, 76829);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Daily_refills_divide_by_number_of_days()
    {
        var project = SampleProject.Create();
        project.Tasks[^1].Date = SampleProject.Date.AddDays(1);

        var report = FuelReport.Build(project);
        var rmto = report.Groups[0].Units[2];

        Assert.Equal(2, rmto.Figures.Dates.Count);
        Assert.Equal(rmto.Figures.Refills.Petrol / 2, rmto.Figures.DailyRefills.Petrol, 10);
        Assert.Equal("10.03.2026 – 11.03.2026", rmto.Figures.DateLabel);
        Assert.Single(rmto.Rows[^1].Figures.Dates);
    }

    [Fact]
    public void Excluded_and_out_of_range_tasks_are_skipped()
    {
        var project = SampleProject.Create();
        foreach (var task in project.Tasks.Where(t => t.Name != "Подвоз ГСМ на ПП"))
            task.IsIncluded = false;

        var report = FuelReport.Build(project);
        Assert.Equal(1, report.TaskCount);
        Assert.Equal("рмто", Assert.Single(Assert.Single(report.Groups).Units).Unit.Name);

        var none = FuelReport.Build(project, new ReportOptions { From = SampleProject.Date.AddDays(1) });
        Assert.True(none.IsEmpty);
    }

    [Fact]
    public void Several_groups_get_grand_total_from_group_masses()
    {
        var project = SampleProject.Create();
        var rp2 = project.Groups[1];
        var unit = new Unit { Name = "1 рдн" };
        var tank = project.Catalog.First(m => m.Name == "ДТ-ёмкость");
        var worker = project.Catalog.First(m => m.Name == "ДТ-работа");
        unit.Vehicles.Add(new UnitVehicle { ModelId = tank.Id, Count = 1000 });
        unit.Vehicles.Add(new UnitVehicle { ModelId = worker.Id, Count = 1 });
        rp2.Units.Add(unit);
        project.Tasks[0].Items.Add(new TaskItem { UnitId = unit.Id, ModelId = worker.Id, Count = 1, WorkHours = 100 });

        var report = FuelReport.Build(project);

        Assert.Equal(2, report.Groups.Count);
        // Одноимённые подразделения в разных рп не смешиваются (ошибка 3.x).
        Assert.Equal(100, report.Groups[1].Figures.Kg.Diesel, 6);
        Assert.Equal(76829 + 1000, report.Total.Mass.Diesel, 6);
        Assert.Equal(14481.8 + 100, report.Total.Kg.Diesel, 6);
    }

    [Fact]
    public void Warns_about_overuse_and_dangling_references()
    {
        var project = SampleProject.Create();
        var task = project.Tasks[0];
        task.Items[0].Count = 5; // в 1 рдн одна «рабочая» машина
        task.Items.Add(new TaskItem { UnitId = Guid.NewGuid(), ModelId = project.Catalog[0].Id, Count = 1 });

        var report = FuelReport.Build(project);

        Assert.Equal(2, report.Warnings.Count);
        Assert.Contains(report.Warnings, w => w.Contains("в наличии 1"));
        Assert.Contains(report.Warnings, w => w.Contains("удалённое"));
    }
}
