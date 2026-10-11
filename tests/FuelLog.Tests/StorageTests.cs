using ClosedXML.Excel;
using FuelLog.Core.Calculation;
using FuelLog.Core.Model;
using FuelLog.Core.Reporting;
using FuelLog.Core.Storage;

namespace FuelLog.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fuellog-tests", Guid.NewGuid().ToString("N"));

    public StorageTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Project_round_trips_through_json()
    {
        var original = SampleProject.Create();
        original.Tasks[0].Note = "Примечание «с кавычками»";
        var path = Path.Combine(_dir, "Проект.gsmproject");

        ProjectStore.Save(original, path);
        var loaded = ProjectStore.Load(path);

        Assert.False(loaded.WasLegacy);
        var p = loaded.Project;
        Assert.Equal(original.Name, p.Name);
        Assert.Equal(original.Catalog.Count, p.Catalog.Count);
        Assert.Equal(original.Groups.SelectMany(g => g.Units).Count(), p.Groups.SelectMany(g => g.Units).Count());
        Assert.Equal(original.Tasks.Count, p.Tasks.Count);
        Assert.Equal(SampleProject.Date, p.Tasks[0].Date);
        Assert.Equal("Примечание «с кавычками»", p.Tasks[0].Note);

        // Тот же отчёт до и после сохранения.
        Assert.Equal(FuelReport.Build(original).Total.Kg, FuelReport.Build(p).Total.Kg);

        var text = File.ReadAllText(path);
        Assert.Contains("\"schemaVersion\": 1", text);
        Assert.Contains("1 рдн", text); // кириллица без \u-экранирования
        Assert.Contains("\"fuel\": \"Diesel\"", text);
    }

    [Fact]
    public void Saving_keeps_backup_of_previous_version()
    {
        var path = Path.Combine(_dir, "p.gsmproject");
        var project = Project.CreateNew("p");
        ProjectStore.Save(project, path);
        project.Name = "изменён";
        ProjectStore.Save(project, path);

        var backups = Directory.GetFiles(ProjectStore.BackupFolder(path), "*.gsmproject");
        Assert.Single(backups);
        Assert.Contains("\"name\": \"p\"", File.ReadAllText(backups[0]));
        Assert.Contains("изменён", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Corrupted_file_gives_readable_error()
    {
        var path = Path.Combine(_dir, "bad.gsmproject");
        File.WriteAllText(path, "{ \"schemaVersion\": 1, \"groups\": [ ");
        var ex = Assert.Throws<ProjectFormatException>(() => ProjectStore.Load(path));
        Assert.Contains("повреждён", ex.Message);
    }

    [Fact]
    public void Newer_schema_is_rejected()
    {
        var path = Path.Combine(_dir, "new.gsmproject");
        File.WriteAllText(path, "{ \"schemaVersion\": 99 }");
        Assert.Throws<ProjectFormatException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void Imports_fuellog3_project_with_sidecar_files()
    {
        // Структура папок и формат FuelLog 3.3.
        Directory.CreateDirectory(Path.Combine(_dir, "Data"));
        Directory.CreateDirectory(Path.Combine(_dir, "Settings", "templates"));
        File.WriteAllText(Path.Combine(_dir, "Старый.gsmproject"), """
            { "Id": "5b3c7a3e-0000-0000-0000-000000000001", "Name": "Старый", "RootPath": "C:\\x",
              "Units": [], "Templates": [], "CurrentTaskPack": { "Tasks": [] },
              "Settings": { "PetrolDensity": 0.8, "DieselDensity": 0.75 }, "Reports": [] }
            """);
        File.WriteAllText(Path.Combine(_dir, "Settings", "projectsettings.json"), """
            { "PetrolDensity": 0.74, "DieselDensity": 0.86, "CustomTaskTypes": ["Разведка", "Марш"], "Theme": "Dark" }
            """);
        File.WriteAllText(Path.Combine(_dir, "Settings", "templates", "Типовой транспорт.gsmtemplate"), """
            { "Id": "5b3c7a3e-0000-0000-0000-000000000002", "Name": "Типовой транспорт", "CreatedAt": "2026-01-01T00:00:00",
              "Vehicles": [
                { "Id": 1, "Name": "КамАЗ-4310", "FuelType": 1, "FuelSupplyMain": 250, "FuelSupplyBank": 125,
                  "FuelConsumptionPer100Km": 40, "FuelConsumptionPerHour": 5, "Category": 7, "Description": null },
                { "Id": 2, "Name": "УАЗ-469", "FuelType": 0, "FuelSupplyMain": 78, "FuelSupplyBank": 0,
                  "FuelConsumptionPer100Km": 16, "FuelConsumptionPerHour": 2, "Category": 16 }
              ] }
            """);
        File.WriteAllText(Path.Combine(_dir, "Data", "units.json"), """
            [
              { "Id": 1, "Name": "1 РП", "Type": 0, "IsBaseGroup": true, "Vehicles": [], "Children": [
                  { "Id": 8, "Name": "1 рдн", "Type": 17, "ParentId": 1, "IsBaseGroup": false, "Children": [], "Vehicles": [
                      { "Id": 1, "Name": "КамАЗ-4310", "FuelType": 1, "FuelSupplyMain": 250, "FuelSupplyBank": 125,
                        "FuelConsumptionPer100Km": 40, "FuelConsumptionPerHour": 5, "Count": 12 },
                      { "Id": 5, "Name": "ЗИЛ-131", "FuelType": 0, "FuelSupplyMain": 170, "FuelSupplyBank": 170,
                        "FuelConsumptionPer100Km": 49, "FuelConsumptionPerHour": 6, "Count": 3 } ] } ] },
              { "Id": 2, "Name": "2 РП", "Type": 1, "IsBaseGroup": true, "Vehicles": [], "Children": [] }
            ]
            """);

        var result = ProjectStore.Load(Path.Combine(_dir, "Старый.gsmproject"));

        Assert.True(result.WasLegacy);
        var p = result.Project;
        Assert.Equal("Старый", p.Name);
        Assert.Equal(0.74, p.Settings.PetrolDensity);
        Assert.Equal(0.86, p.Settings.DieselDensity);
        Assert.Equal(["Разведка", "Марш"], p.Settings.TaskTypes);

        Assert.Equal(["КамАЗ-4310", "УАЗ-469", "ЗИЛ-131"], p.Catalog.Select(m => m.Name));
        var kamaz = p.Catalog[0];
        Assert.Equal(FuelType.Diesel, kamaz.Fuel);
        Assert.Equal(375, kamaz.TankLiters);
        Assert.Equal("РВО", kamaz.Category);
        Assert.Equal("Связь", p.Catalog[1].Category);

        Assert.Equal(["1 РП", "2 РП"], p.Groups.Select(g => g.Name));
        var unit = Assert.Single(p.Groups[0].Units);
        Assert.Equal("1 рдн", unit.Name);
        Assert.Equal(12, unit.Vehicles.Single(v => v.ModelId == kamaz.Id).Count);
        Assert.Equal(3, unit.Vehicles.Single(v => v.ModelId == p.Catalog[2].Id).Count);
    }

    [Fact]
    public void Catalog_exchange_merges_by_name_and_keeps_references()
    {
        var project = SampleProject.Create();
        var model = project.Catalog[0];
        var path = Path.Combine(_dir, "справочник.gsmtemplate");

        var exported = new VehicleModel { Name = model.Name.ToUpperInvariant(), Fuel = model.Fuel, MainTankLiters = 5, Category = "Новая" };
        CatalogExchange.Export([exported, new VehicleModel { Name = "Новая марка" }], path);

        var result = CatalogExchange.Merge(project, CatalogExchange.Read(path));

        Assert.Equal(new CatalogMergeResult(Added: 1, Updated: 1), result);
        Assert.Same(model, project.Catalog[0]);
        Assert.Equal(5, model.MainTankLiters);
        Assert.Contains("Новая", project.Settings.Categories);
    }

    [Fact]
    public void Excel_report_follows_sample_layout()
    {
        var path = Path.Combine(_dir, "report.xlsx");
        var report = FuelReport.Build(SampleProject.Create());

        ExcelReportWriter.Write(report, path);

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Расчёт");
        Assert.Equal("Воинская часть (рп)", ws.Cell("A1").GetString());
        Assert.Equal("Расход", ws.Cell("E1").GetString());
        Assert.Equal("Масса заправки (кг)", ws.Cell("I1").GetString());
        Assert.Equal("Среднесуточный расход (запр)", ws.Cell("K1").GetString());
        Assert.Equal("АБ", ws.Cell("E4").GetString());
        Assert.Equal("запр", ws.Cell("H5").GetString());
        Assert.True(ws.Cell("E1").IsMerged());

        Assert.Equal("1рп", ws.Cell("A6").GetString());
        Assert.Equal("1 рдн", ws.Cell("B6").GetString());
        Assert.Equal("Инженерная подготовка ПП", ws.Cell("C6").GetString());
        Assert.Equal(new DateTime(2026, 3, 10), ws.Cell("D6").GetDateTime());
        Assert.Equal(91.26, ws.Cell("E6").GetDouble(), 6);
        Assert.Equal(766, ws.Cell("I6").GetDouble(), 6);

        // Итог подразделения сразу после его строк (строка 10, как в образце).
        Assert.Equal("Итого 1 рдн 1рп", ws.Cell("A10").GetString());
        Assert.True(ws.Cell("A10").IsMerged());
        Assert.Equal("Итого 2 рдн 1рп", ws.Cell("A15").GetString());
        Assert.Equal("Итого рмто 1рп", ws.Cell("A19").GetString());
        Assert.Equal("Итого 1рп", ws.Cell("A20").GetString());
        Assert.Equal(2987, ws.Cell("I20").GetDouble(), 6);
        Assert.True(ws.Cell("A21").IsEmpty());

        var details = wb.Worksheet("Детализация");
        Assert.Equal(report.Details.Count + 1, details.LastRowUsed()!.RowNumber());
        Assert.Equal("Плотность АБ, кг/л", wb.Worksheet("Параметры").Cell("A4").GetString());
    }

    [Fact]
    public void Change_tracker_sees_nested_changes_and_new_items()
    {
        var project = SampleProject.Create();
        using var tracker = new ChangeTracker(project);
        var changes = 0;
        tracker.Changed += (_, _) => changes++;

        project.Tasks[0].Items[0].Count = 3;
        Assert.Equal(1, changes);

        var task = new FuelTask();
        project.Tasks.Add(task);
        Assert.Equal(2, changes);
        task.Items.Add(new TaskItem());
        Assert.Equal(3, changes);
        task.Items[0].WorkHours = 2;
        Assert.Equal(4, changes);

        project.Tasks.Remove(task);
        Assert.Equal(5, changes);
        task.Name = "отсоединена";
        Assert.Equal(5, changes);

        project.Settings.TaskTypes.Clear();
        Assert.Equal(6, changes);
        project.Name = "новое имя";
        Assert.Equal(7, changes);
    }

    [Fact]
    public void App_settings_keep_recent_projects_unique_and_limited()
    {
        var path = Path.Combine(_dir, "settings.json");
        var settings = new AppSettings();
        for (var i = 0; i < 20; i++)
            settings.Touch(Path.Combine(_dir, $"{i}.gsmproject"), $"П{i}");
        settings.Touch(Path.Combine(_dir, "5.gsmproject"), "П5");
        settings.Save(path);

        var loaded = AppSettings.Load(path);
        Assert.Equal(AppSettings.MaxRecent, loaded.Recent.Count);
        Assert.Equal("П5", loaded.Recent[0].Name);
        Assert.Single(loaded.Recent, r => r.Name == "П5");
    }
}
