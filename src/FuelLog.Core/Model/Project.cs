using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FuelLog.Core.Model;

/// <summary>Проект: справочник техники, оргструктура, задачи и настройки. Хранится одним JSON-файлом.</summary>
public sealed partial class Project : ObservableObject
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;
    public ProjectSettings Settings { get; set; } = new();
    public ObservableCollection<VehicleModel> Catalog { get; set; } = [];
    public ObservableCollection<Group> Groups { get; set; } = [];
    public ObservableCollection<FuelTask> Tasks { get; set; } = [];

    public VehicleModel? FindModel(Guid id) => Catalog.FirstOrDefault(m => m.Id == id);

    [JsonIgnore]
    public IEnumerable<Unit> AllUnits => Groups.SelectMany(g => g.Units);

    public (Group Group, Unit Unit)? FindUnit(Guid id)
    {
        foreach (var group in Groups)
            foreach (var unit in group.Units)
                if (unit.Id == id)
                    return (group, unit);
        return null;
    }

    /// <summary>Новый проект с типовой структурой воинских частей (как в прежней версии).</summary>
    public static Project CreateNew(string name)
    {
        var project = new Project { Name = name };
        foreach (var groupName in new[] { "1 рп", "2 рп", "3 рп", "4 рп", "ртб", "трб", "поо" })
            project.Groups.Add(new Group { Name = groupName });
        return project;
    }
}

public sealed partial class ProjectSettings : ObservableObject
{
    public static readonly string[] DefaultTaskTypes =
    [
        "Совершение марша",
        "Разведка",
        "Инженерная подготовка",
        "Несение дежурства",
        "Подвоз ГСМ",
        "Подвоз боеприпасов",
        "Подвоз продовольствия",
        "Подвоз воды",
        "Медицинская эвакуация",
        "Другое",
    ];

    public static readonly string[] DefaultCategories =
        ["РВО", "АС", "НС", "РХБЗ", "ПС", "ВС", "ГСМ", "Мед Сл", "ППЗ и СР", "Связь", "Другое"];

    /// <summary>Плотность автобензина, кг/л.</summary>
    [ObservableProperty]
    public partial double PetrolDensity { get; set; } = 0.75;

    /// <summary>Плотность дизельного топлива, кг/л.</summary>
    [ObservableProperty]
    public partial double DieselDensity { get; set; } = 0.85;

    public ObservableCollection<string> TaskTypes { get; set; } = new(DefaultTaskTypes);
    public ObservableCollection<string> Categories { get; set; } = new(DefaultCategories);

    public double DensityOf(FuelType fuel) => fuel == FuelType.Petrol ? PetrolDensity : DieselDensity;
}

public enum FuelType
{
    /// <summary>АБ — автобензин.</summary>
    Petrol,

    /// <summary>ДТ — дизельное топливо.</summary>
    Diesel,
}

/// <summary>Марка техники из справочника.</summary>
public sealed partial class VehicleModel : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FuelName))]
    public partial FuelType Fuel { get; set; }

    /// <summary>Основной бак, л.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TankLiters))]
    public partial double MainTankLiters { get; set; }

    /// <summary>Дополнительный бак, л.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TankLiters))]
    public partial double ExtraTankLiters { get; set; }

    /// <summary>Норма расхода на марше, л/100 км.</summary>
    [ObservableProperty]
    public partial double LitersPer100Km { get; set; }

    /// <summary>Норма расхода при работе на месте, л/ч.</summary>
    [ObservableProperty]
    public partial double LitersPerHour { get; set; }

    [ObservableProperty]
    public partial string Category { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>Полная заправка (оба бака), л.</summary>
    [JsonIgnore]
    public double TankLiters => MainTankLiters + ExtraTankLiters;

    [JsonIgnore]
    public string FuelName => Fuel.ShortName();
}

/// <summary>Воинская часть (рп) — верхний уровень структуры и отчёта.</summary>
public sealed partial class Group : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    public ObservableCollection<Unit> Units { get; set; } = [];
}

/// <summary>Подразделение, за которым закреплена техника.</summary>
public sealed partial class Unit : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    public ObservableCollection<UnitVehicle> Vehicles { get; set; } = [];
}

/// <summary>Сколько единиц марки числится в подразделении.</summary>
public sealed partial class UnitVehicle : ObservableObject
{
    public Guid ModelId { get; set; }

    [ObservableProperty]
    public partial int Count { get; set; }
}

/// <summary>Задача, выполняемая техникой подразделений в определённый день.</summary>
public sealed partial class FuelTask : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TaskType { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Участвует ли задача в расчёте отчёта.</summary>
    [ObservableProperty]
    public partial bool IsIncluded { get; set; } = true;

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    public ObservableCollection<TaskItem> Items { get; set; } = [];

    public FuelTask Clone() => new()
    {
        Name = Name,
        TaskType = TaskType,
        Date = Date,
        IsIncluded = IsIncluded,
        Note = Note,
        Items = new(Items.Select(i => i.Clone())),
    };
}

/// <summary>Техника одной марки одного подразделения, задействованная в задаче.</summary>
public sealed partial class TaskItem : ObservableObject
{
    public Guid UnitId { get; set; }
    public Guid ModelId { get; set; }

    [ObservableProperty]
    public partial int Count { get; set; }

    /// <summary>Протяжённость марша, км.</summary>
    [ObservableProperty]
    public partial double DistanceKm { get; set; }

    /// <summary>Время работы на месте, ч.</summary>
    [ObservableProperty]
    public partial double WorkHours { get; set; }

    /// <summary>Ожидаемые потери техники, %.</summary>
    [ObservableProperty]
    public partial double LossPercent { get; set; }

    public TaskItem Clone() => new()
    {
        UnitId = UnitId,
        ModelId = ModelId,
        Count = Count,
        DistanceKm = DistanceKm,
        WorkHours = WorkHours,
        LossPercent = LossPercent,
    };
}

public static class FuelTypeExtensions
{
    public static string ShortName(this FuelType fuel) => fuel == FuelType.Petrol ? "АБ" : "ДТ";
}
