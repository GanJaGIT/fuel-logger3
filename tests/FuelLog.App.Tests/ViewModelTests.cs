using System.IO;
using FuelLog.App.Services;
using FuelLog.App.ViewModels;
using FuelLog.Core.Model;
using FuelLog.Core.Storage;

namespace FuelLog.App.Tests;

/// <summary>Диалоги без окон: подтверждения отвечают заданным значением, ввод — из очереди.</summary>
internal sealed class FakeDialogs : IDialogService
{
    public bool ConfirmAnswer { get; set; } = true;
    public Queue<string> PromptAnswers { get; } = new();
    public List<string> Messages { get; } = [];
    public List<string> Opened { get; } = [];

    public void Info(string message, string title = "FuelLog") => Messages.Add(message);
    public void Warn(string message, string title = "Внимание") => Messages.Add("WARN: " + message);
    public void Error(string message, string title = "Ошибка") => Messages.Add("ERROR: " + message);
    public bool Confirm(string message, string title = "Подтверждение") { Messages.Add(message); return ConfirmAnswer; }
    public string? Prompt(string title, string label, string initial = "") => PromptAnswers.Count > 0 ? PromptAnswers.Dequeue() : null;
    public string? OpenFile(string title, string filter) => null;
    public string? SaveFile(string title, string filter, string fileName, string? folder = null) => null;
    public void OpenWithShell(string path) => Opened.Add(path);
    public void ShowInExplorer(string path) { }
}

public sealed class ViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fuellog-app-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new();

    public ViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static (Project Project, Unit Unit, VehicleModel Kamaz, VehicleModel Uaz) Sample()
    {
        var project = Project.CreateNew("Тест");
        var kamaz = new VehicleModel { Name = "КамАЗ", Fuel = FuelType.Diesel, MainTankLiters = 200, LitersPer100Km = 40, LitersPerHour = 5 };
        var uaz = new VehicleModel { Name = "УАЗ", Fuel = FuelType.Petrol, MainTankLiters = 80, LitersPer100Km = 16, LitersPerHour = 2 };
        project.Catalog.Add(kamaz);
        project.Catalog.Add(uaz);
        var unit = new Unit { Name = "1 рдн" };
        unit.Vehicles.Add(new UnitVehicle { ModelId = kamaz.Id, Count = 5 });
        unit.Vehicles.Add(new UnitVehicle { ModelId = uaz.Id, Count = 2 });
        project.Groups[0].Units.Add(unit);
        return (project, unit, kamaz, uaz);
    }

    [Fact]
    public void Tasks_add_vehicles_from_unit_and_compute_totals()
    {
        var (project, unit, kamaz, _) = Sample();
        var vm = new TasksViewModel(project, _dialogs);
        vm.Refresh();

        vm.AddTaskCommand.Execute(null);
        var task = vm.SelectedTask!;
        Assert.Equal(project.Settings.TaskTypes[0], task.Name);

        vm.PickUnit = vm.UnitChoices.Single(c => c.Unit == unit);
        Assert.Equal(2, vm.PickRows.Count);
        vm.DefaultDistanceKm = 100;
        vm.PickRows.Single(r => r.Model == kamaz).Count = 99; // ограничивается наличием
        Assert.Equal(5, vm.PickRows.Single(r => r.Model == kamaz).Count);
        vm.PickRows.Single(r => r.Model == kamaz).Count = 3;
        vm.AddPickedCommand.Execute(null);

        var row = Assert.Single(vm.ItemRows);
        Assert.Equal(3, row.Count);
        Assert.Equal(3 * 40, row.Liters, 6);
        Assert.Equal(3 * 40 * 0.85, vm.Totals.DieselKg, 6);

        row.WorkHours = 2; // правка в таблице пересчитывает итоги
        Assert.Equal((120 + 3 * 2 * 5) * 0.85, vm.Totals.DieselKg, 6);

        row.Count = 7; // больше, чем есть в подразделении
        Assert.True(row.IsOverAvailable);
        Assert.Equal(1, vm.Totals.Problems);
    }

    [Fact]
    public void Add_buttons_become_available_for_the_very_first_task()
    {
        // Ошибка 4.0: кнопки добавления техники оживали только после создания второй задачи.
        var (project, unit, _, _) = Sample();
        var vm = new TasksViewModel(project, _dialogs);
        vm.Refresh();
        var raised = 0;
        vm.AddPickedCommand.CanExecuteChanged += (_, _) => raised++;

        vm.AddTaskCommand.Execute(null);
        Assert.False(vm.AddPickedCommand.CanExecute(null));

        vm.PickUnit = vm.UnitChoices.Single(c => c.Unit == unit);
        Assert.True(raised >= 2);
        Assert.True(vm.AddPickedCommand.CanExecute(null));
        Assert.True(vm.AddWholeUnitCommand.CanExecute(null));

        vm.AddWholeUnitCommand.Execute(null);
        Assert.Equal(2, vm.ItemRows.Count);
    }

    [Fact]
    public void Structure_checks_whole_group_and_aggregates_by_model()
    {
        var (project, unit, kamaz, _) = Sample();
        var second = new Unit { Name = "2 рдн" };
        second.Vehicles.Add(new UnitVehicle { ModelId = kamaz.Id, Count = 3 });
        project.Groups[0].Units.Add(second);
        var vm = new StructureViewModel(project, _dialogs);

        Assert.True(vm.IsNothingChecked);
        vm.Nodes[0].IsChecked = true; // галочка у воинской части

        Assert.Equal(2, vm.CheckedCount);
        Assert.True(vm.Nodes[0].IsChecked);
        Assert.Equal("1 рп — все подразделения (2)", vm.HeaderTitle);
        var kamazRow = vm.ModelRows.Single(r => r.Model == kamaz);
        Assert.Equal(8, kamazRow.Count);
        Assert.Equal(2, vm.UnitSummaryRows.Count);
        Assert.Equal(8 * 200 * 0.85, vm.Summary!.Kg.Diesel, 6);

        vm.Nodes[0].Units[1].IsChecked = false;
        Assert.Null(vm.Nodes[0].IsChecked); // отмечена часть подразделений
        Assert.True(vm.IsSingleChecked);
        Assert.Equal(2, vm.UnitRows.Count);

        vm.CheckAllCommand.Execute(null);
        Assert.Equal(2, vm.CheckedCount);
        vm.UncheckAllCommand.Execute(null);
        Assert.True(vm.IsNothingChecked);
        Assert.Equal("Весь проект", vm.SummaryCaption);

        vm.CheckOnlyCommand.Execute(vm.Nodes[0].Units[1]);
        Assert.Same(second, vm.SingleUnit!.Unit);
        Assert.Contains(unit, project.Groups[0].Units);
    }

    [Fact]
    public void Adding_vehicle_requires_exactly_one_checked_unit()
    {
        var (project, unit, _, uaz) = Sample();
        project.Groups[0].Units.Add(new Unit { Name = "2 рдн" });
        var vm = new StructureViewModel(project, _dialogs) { AddModel = uaz, AddCount = 4 };
        Assert.True(vm.AddVehicleCommand.CanExecute(null));

        vm.AddVehicleCommand.Execute(null); // ничего не отмечено
        vm.CheckAllCommand.Execute(null);
        vm.AddVehicleCommand.Execute(null); // отмечено два

        Assert.Equal(2, _dialogs.Messages.Count(m => m.StartsWith("WARN:")));
        Assert.Contains(_dialogs.Messages, m => m.Contains("отмечено 2"));
        Assert.Equal(2, unit.Vehicles.Single(v => v.ModelId == uaz.Id).Count);

        vm.CheckOnlyCommand.Execute(vm.Nodes[0].Units.Single(n => n.Unit == unit));
        vm.AddVehicleCommand.Execute(null);
        Assert.Equal(6, unit.Vehicles.Single(v => v.ModelId == uaz.Id).Count);
    }

    [Fact]
    public void Structure_keeps_checks_when_tree_changes_and_remembers_panel_state()
    {
        var (project, unit, _, _) = Sample();
        var settingsPath = Path.Combine(_dir, "settings.json");
        var settings = new AppSettings { FilePath = settingsPath };
        var vm = new StructureViewModel(project, _dialogs, settings);
        vm.CheckOnlyCommand.Execute(vm.Nodes[0].Units[0]);

        project.Groups[0].Units.Add(new Unit { Name = "новое" }); // перестройка дерева
        Assert.Same(unit, vm.SingleUnit!.Unit);

        vm.SelectedNode = unit;
        vm.MoveDownCommand.Execute(null);
        Assert.Equal("новое", project.Groups[0].Units[0].Name);
        Assert.True(vm.Nodes[0].Units[1].IsSelected);
        Assert.True(vm.Nodes[0].Units[1].IsChecked);

        Assert.True(vm.IsPanelOpen);
        vm.TogglePanelCommand.Execute(null);
        Assert.False(settings.StructurePanelOpen);
        Assert.False(new StructureViewModel(project, _dialogs, settings).IsPanelOpen);
        Assert.False(AppSettings.Load(settingsPath).StructurePanelOpen);
    }

    [Fact]
    public void Sample_project_ships_next_to_the_app_and_is_valid()
    {
        Assert.True(File.Exists(MainViewModel.SampleSourcePath), MainViewModel.SampleSourcePath);
        var sample = ProjectStore.Load(MainViewModel.SampleSourcePath).Project;
        Assert.NotEmpty(sample.Catalog);
        Assert.NotEmpty(sample.Tasks);
        Assert.False(Core.Calculation.FuelReport.Build(sample).IsEmpty);
    }

    [Fact]
    public void Task_name_follows_type_until_user_renames_it()
    {
        var (project, _, _, _) = Sample();
        var vm = new TasksViewModel(project, _dialogs);
        vm.AddTaskCommand.Execute(null);
        var task = vm.SelectedTask!;

        task.TaskType = "Разведка";
        Assert.Equal("Разведка", task.Name);

        task.Name = "Разведка маршрута";
        task.TaskType = "Подвоз воды";
        Assert.Equal("Разведка маршрута", task.Name);
    }

    [Fact]
    public void Duplicate_task_copies_items_and_delete_selects_neighbour()
    {
        var (project, unit, kamaz, _) = Sample();
        var vm = new TasksViewModel(project, _dialogs);
        vm.AddTaskCommand.Execute(null);
        vm.SelectedTask!.Items.Add(new TaskItem { UnitId = unit.Id, ModelId = kamaz.Id, Count = 2 });

        vm.DuplicateTaskCommand.Execute(null);
        Assert.Equal(2, project.Tasks.Count);
        Assert.NotSame(project.Tasks[0].Items[0], project.Tasks[1].Items[0]);
        Assert.Equal(2, vm.SelectedTask!.Items[0].Count);

        vm.DeleteTaskCommand.Execute(null);
        Assert.Single(project.Tasks);
        Assert.Same(project.Tasks[0], vm.SelectedTask);
    }

    [Fact]
    public void Deleting_unit_removes_its_task_items_after_confirmation()
    {
        var (project, unit, kamaz, _) = Sample();
        project.Tasks.Add(new FuelTask { Items = { new TaskItem { UnitId = unit.Id, ModelId = kamaz.Id, Count = 1 } } });
        var vm = new StructureViewModel(project, _dialogs) { SelectedNode = unit };

        _dialogs.ConfirmAnswer = false;
        vm.DeleteCommand.Execute(null);
        Assert.Contains(unit, project.Groups[0].Units);

        _dialogs.ConfirmAnswer = true;
        vm.DeleteCommand.Execute(null);
        Assert.DoesNotContain(unit, project.Groups[0].Units);
        Assert.Empty(project.Tasks[0].Items);
        Assert.Contains(_dialogs.Messages, m => m.Contains("строк задач: 1"));
    }

    [Fact]
    public void Structure_adds_units_and_vehicles()
    {
        var (project, _, _, uaz) = Sample();
        var vm = new StructureViewModel(project, _dialogs) { SelectedNode = project.Groups[1] };

        _dialogs.PromptAnswers.Enqueue("рмто");
        vm.AddUnitCommand.Execute(null);
        var unit = Assert.IsType<Unit>(vm.SelectedNode);
        Assert.Equal("рмто", unit.Name);
        Assert.Contains(unit, project.Groups[1].Units);

        vm.AddModel = uaz;
        vm.AddCount = 3;
        vm.AddVehicleCommand.Execute(null);
        vm.AddModel = uaz;
        vm.AddCount = 2;
        vm.AddVehicleCommand.Execute(null);

        var row = Assert.Single(vm.UnitRows);
        Assert.Equal(5, row.Count);
        Assert.Equal(5 * 80 * 0.75, vm.Summary!.Kg.Petrol, 6);
    }

    [Fact]
    public void Catalog_delete_reports_usage_and_cleans_references()
    {
        var (project, unit, kamaz, _) = Sample();
        project.Tasks.Add(new FuelTask { Items = { new TaskItem { UnitId = unit.Id, ModelId = kamaz.Id, Count = 1 } } });
        var vm = new CatalogViewModel(project, _dialogs) { SelectedModel = kamaz };

        vm.DeleteCommand.Execute(null);

        Assert.DoesNotContain(kamaz, project.Catalog);
        Assert.DoesNotContain(unit.Vehicles, v => v.ModelId == kamaz.Id);
        Assert.Empty(project.Tasks[0].Items);
        Assert.Contains(_dialogs.Messages, m => m.Contains("подразделений — 1, строк задач — 1"));
    }

    [Fact]
    public void Catalog_filter_by_search_and_category()
    {
        var (project, _, kamaz, _) = Sample();
        kamaz.Category = "АС";
        var vm = new CatalogViewModel(project, _dialogs);

        vm.SearchText = "кам";
        Assert.Equal([kamaz], vm.Models.Cast<VehicleModel>());
        vm.SearchText = string.Empty;
        vm.CategoryFilter = "АС";
        Assert.Equal([kamaz], vm.Models.Cast<VehicleModel>());
        Assert.Equal("Показано 1 из 2", vm.CountText);
    }

    [Fact]
    public void Report_preview_and_export_write_excel_next_to_project()
    {
        var (project, unit, kamaz, _) = Sample();
        project.Tasks.Add(new FuelTask { Name = "Марш", Items = { new TaskItem { UnitId = unit.Id, ModelId = kamaz.Id, Count = 2, DistanceKm = 50 } } });
        var path = Path.Combine(_dir, "Тест.gsmproject");
        ProjectStore.Save(project, path);
        using var session = new ProjectSession(project, path);
        var vm = new ReportViewModel(session, _dialogs);

        vm.Refresh();
        // Строка задачи, итог подразделения, итог воинской части.
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal(PreviewRowKind.GroupTotal, vm.Rows[^1].Kind);

        vm.ExportCommand.Execute(null);
        var file = Assert.Single(Directory.GetFiles(session.ReportsFolder, "*.xlsx"));
        Assert.Equal(file, Assert.Single(_dialogs.Opened));
        Assert.Single(vm.Files);
    }

    [Fact]
    public void Session_saves_changes_and_detects_external_edits()
    {
        var (project, _, _, _) = Sample();
        var path = Path.Combine(_dir, "s.gsmproject");
        ProjectStore.Save(project, path);
        using var session = new ProjectSession(project, path);

        project.Name = "Новое имя";
        Assert.True(session.HasUnsavedChanges);
        Assert.True(session.Save());
        Assert.False(session.HasUnsavedChanges);
        Assert.Contains("Новое имя", File.ReadAllText(path));

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5)); // «другая копия программы»
        session.ConfirmOverwriteExternalChange = () => false;
        project.Name = "Ещё имя";
        Assert.False(session.Save());
        Assert.True(session.HasSaveError);
        Assert.DoesNotContain("Ещё имя", File.ReadAllText(path));
    }

    [Fact]
    public void List_editor_adds_moves_and_removes()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string> { "А", "Б" };
        var editor = new ListEditor(items, ["А"], "тип", _dialogs) { NewItem = " В " };

        editor.AddCommand.Execute(null);
        Assert.Equal(["А", "Б", "В"], items);
        editor.MoveUpCommand.Execute(null);
        Assert.Equal(["А", "В", "Б"], items);
        editor.RemoveCommand.Execute(null);
        Assert.Equal(["А", "Б"], items);
        Assert.Equal("Б", editor.Selected);
    }
}
