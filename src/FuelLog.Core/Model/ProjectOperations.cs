namespace FuelLog.Core.Model;

public sealed record ModelUsage(int Units, int TaskItems);

/// <summary>Операции, затрагивающие связи между частями проекта (удаление с очисткой ссылок).</summary>
public static class ProjectOperations
{
    public static int TaskItemsOf(this Project project, Unit unit) =>
        project.Tasks.Sum(t => t.Items.Count(i => i.UnitId == unit.Id));

    public static ModelUsage UsageOf(this Project project, VehicleModel model) => new(
        project.AllUnits.Count(u => u.Vehicles.Any(v => v.ModelId == model.Id)),
        project.Tasks.Sum(t => t.Items.Count(i => i.ModelId == model.Id)));

    /// <summary>Удаляет подразделение и строки задач, где была задействована его техника.</summary>
    public static void RemoveUnit(this Project project, Group group, Unit unit)
    {
        group.Units.Remove(unit);
        RemoveTaskItems(project, i => i.UnitId == unit.Id);
    }

    public static void RemoveGroup(this Project project, Group group)
    {
        var unitIds = group.Units.Select(u => u.Id).ToHashSet();
        project.Groups.Remove(group);
        RemoveTaskItems(project, i => unitIds.Contains(i.UnitId));
    }

    /// <summary>Удаляет марку из справочника, из подразделений и из задач.</summary>
    public static void RemoveModel(this Project project, VehicleModel model)
    {
        project.Catalog.Remove(model);
        foreach (var unit in project.AllUnits)
            foreach (var vehicle in unit.Vehicles.Where(v => v.ModelId == model.Id).ToList())
                unit.Vehicles.Remove(vehicle);
        RemoveTaskItems(project, i => i.ModelId == model.Id);
    }

    private static void RemoveTaskItems(Project project, Func<TaskItem, bool> predicate)
    {
        foreach (var task in project.Tasks)
            foreach (var item in task.Items.Where(predicate).ToList())
                task.Items.Remove(item);
    }

    /// <summary>Сдвигает элемент коллекции на одну позицию вверх (-1) или вниз (+1).</summary>
    public static bool Move<T>(this System.Collections.ObjectModel.ObservableCollection<T> list, T item, int direction)
    {
        var index = list.IndexOf(item);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= list.Count)
            return false;
        list.Move(index, target);
        return true;
    }
}
