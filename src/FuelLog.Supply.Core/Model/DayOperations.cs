namespace FuelLog.Supply.Core.Model;

/// <summary>Ведение документа по суткам.</summary>
public static class DayOperations
{
    /// <summary>Дата следующих суток: следующий день после последних, для пустого документа — сегодня.</summary>
    public static DateOnly NextDate(SupplyProject project, DateOnly today) =>
        project.Days.Count == 0 ? today : project.Days.Max(d => d.Date).AddDays(1);

    /// <summary>
    /// Добавляет сутки. Состояние «топливо в транспорте» переносится с предыдущих суток:
    /// загрузка и разгрузка учитываются только в день переключения.
    /// </summary>
    public static SupplyDay AddDay(SupplyProject project, DateOnly date)
    {
        if (project.Days.Any(d => d.Date == date))
            throw new InvalidOperationException($"Сутки {date:dd.MM.yyyy} уже есть в документе.");

        var day = new SupplyDay { Date = date };
        var previous = project.Days.Where(d => d.Date < date).OrderBy(d => d.Date).LastOrDefault();
        if (previous is not null)
        {
            foreach (var state in previous.Groups.Where(g => g.InTransport))
                day.Groups.Add(new GroupDayEntry { GroupId = state.GroupId, Fuel = state.Fuel, InTransport = true });
        }

        var index = project.Days.TakeWhile(d => d.Date < date).Count();
        project.Days.Insert(index, day);
        return day;
    }

    /// <summary>Удалять можно только последние сутки, чтобы не разорвать цепочку «наличие → остаток».</summary>
    public static bool CanRemove(SupplyProject project, SupplyDay day) =>
        project.Days.Count > 0 && project.Days.MaxBy(d => d.Date) == day;
}
