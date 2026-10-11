using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using FuelLog.Core.Model;

namespace FuelLog.Supply.Core.Model;

/// <summary>
/// Проект модели 2 «Потребность и автономность»: структура рд с нормами запасов,
/// привязанные проекты FuelLog (модель 1), транспорт подвоза и документ по суткам.
/// </summary>
public sealed partial class SupplyProject : ObservableObject
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    public SupplySettings Settings { get; set; } = new();

    /// <summary>Проекты FuelLog, из которых берутся техника (масса заправки) и расход на выполнение задач.</summary>
    public ObservableCollection<LinkedProject> Links { get; set; } = [];

    public ObservableCollection<SupplyGroup> Groups { get; set; } = [];

    /// <summary>Склад рд (главный): получает ГСМ от округа и выдаёт на склады частей.</summary>
    [ObservableProperty]
    public partial Guid? MainWarehouseRowId { get; set; }

    /// <summary>Типовой транспорт подвоза: марки и вместимость цистерн.</summary>
    public ObservableCollection<TransportModel> TransportCatalog { get; set; } = [];

    /// <summary>Документ по суткам, по возрастанию даты.</summary>
    public ObservableCollection<SupplyDay> Days { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<SupplyRow> AllRows => Groups.SelectMany(g => g.Rows);

    public (SupplyGroup Group, SupplyRow Row)? FindRow(Guid? id)
    {
        if (id is null)
            return null;
        foreach (var group in Groups)
            foreach (var row in group.Rows)
                if (row.Id == id)
                    return (group, row);
        return null;
    }

    public SupplyGroup? GroupOf(SupplyRow row) => Groups.FirstOrDefault(g => g.Rows.Contains(row));

    public TransportModel? FindTransport(Guid id) => TransportCatalog.FirstOrDefault(t => t.Id == id);
}

public sealed partial class SupplySettings : ObservableObject
{
    /// <summary>Плотности для перевода литров транспорта подвоза в килограммы.</summary>
    [ObservableProperty]
    public partial double PetrolDensity { get; set; } = 0.75;

    [ObservableProperty]
    public partial double DieselDensity { get; set; } = 0.85;

    public double DensityOf(FuelType fuel) => fuel == FuelType.Petrol ? PetrolDensity : DieselDensity;
}

/// <summary>Привязанный проект FuelLog (модель 1).</summary>
public sealed partial class LinkedProject : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Путь к файлу *.gsmproject — относительно файла модели 2, если они на одном диске.</summary>
    [ObservableProperty]
    public partial string Path { get; set; } = string.Empty;

    /// <summary>Название проекта на момент последнего чтения (для отображения, если файл недоступен).</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;
}

/// <summary>Воинская часть (управление, рп, трб) — группа строк документа.</summary>
public sealed partial class SupplyGroup : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    public ObservableCollection<SupplyRow> Rows { get; set; } = [];

    /// <summary>Склад части: из него дозаправляется техника части и загружается транспорт подвоза; на него идёт подвоз.</summary>
    [ObservableProperty]
    public partial Guid? WarehouseRowId { get; set; }

    /// <summary>Подразделение подвоза (БМТО, РМТО), которому принадлежит транспорт.</summary>
    [ObservableProperty]
    public partial Guid? TransportRowId { get; set; }

    /// <summary>Наличие транспорта подвоза у подразделения подвоза.</summary>
    public ObservableCollection<TransportItem> Transport { get; set; } = [];
}

public enum RowKind
{
    /// <summary>Подразделение с техникой (масса заправки из модели 1).</summary>
    Unit,

    /// <summary>Склад: своей техники нет, норма считается от массы заправки других подразделений.</summary>
    Warehouse,
}

/// <summary>Строка документа: подразделение или склад.</summary>
public sealed partial class SupplyRow : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial RowKind Kind { get; set; }

    /// <summary>Привязка к подразделению модели 1: проект и подразделение.</summary>
    [ObservableProperty]
    public partial Guid? LinkProjectId { get; set; }

    [ObservableProperty]
    public partial Guid? LinkUnitId { get; set; }

    /// <summary>Установленные запасы: Σ коэффициент × масса заправки области.</summary>
    public ObservableCollection<NormTerm> Norm { get; set; } = [];

    /// <summary>Начальное наличие, кг. null — по умолчанию (подразделение — 1 заправка, склад — 0).</summary>
    [ObservableProperty]
    public partial double? StartPetrolKg { get; set; }

    [ObservableProperty]
    public partial double? StartDieselKg { get; set; }

    [JsonIgnore]
    public bool IsLinked => LinkProjectId is not null && LinkUnitId is not null;

    /// <summary>Установленные запасы в заправках (сумма коэффициентов).</summary>
    [JsonIgnore]
    public double NormRefills => Norm.Sum(t => t.Coefficient);

    public double? StartKg(FuelType fuel) => fuel == FuelType.Petrol ? StartPetrolKg : StartDieselKg;

    public void SetStartKg(FuelType fuel, double? value)
    {
        if (fuel == FuelType.Petrol)
            StartPetrolKg = value;
        else
            StartDieselKg = value;
    }

    /// <summary>Простая норма: N заправок своей техники.</summary>
    public static ObservableCollection<NormTerm> OwnNorm(double refills) => [new NormTerm { Coefficient = refills, Scope = NormScope.Own }];
}

public enum NormScope
{
    /// <summary>Своя техника подразделения.</summary>
    Own,

    /// <summary>Вся техника своей воинской части (рп, трб).</summary>
    Group,

    /// <summary>Вся техника рд (все части).</summary>
    Division,

    /// <summary>Техника выбранных воинских частей.</summary>
    SelectedGroups,
}

/// <summary>Слагаемое нормы: коэффициент × масса заправки области (Мз).</summary>
public sealed partial class NormTerm : ObservableObject
{
    [ObservableProperty]
    public partial double Coefficient { get; set; }

    [ObservableProperty]
    public partial NormScope Scope { get; set; }

    /// <summary>Для <see cref="NormScope.SelectedGroups"/>: какие части учитываются.</summary>
    public ObservableCollection<Guid> GroupIds { get; set; } = [];
}

/// <summary>Марка транспорта подвоза (типовой транспорт).</summary>
public sealed partial class TransportModel : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial FuelType Fuel { get; set; } = FuelType.Diesel;

    /// <summary>Вместимость цистерны одной единицы, л.</summary>
    [ObservableProperty]
    public partial double CapacityLiters { get; set; }
}

public sealed partial class TransportItem : ObservableObject
{
    public Guid ModelId { get; set; }

    [ObservableProperty]
    public partial int Count { get; set; }
}

/// <summary>Сутки документа: исходные данные дня (операции, потери). Всё остальное рассчитывается.</summary>
public sealed partial class SupplyDay : ObservableObject
{
    [ObservableProperty]
    public partial DateOnly Date { get; set; }

    public ObservableCollection<RowDayEntry> Rows { get; set; } = [];
    public ObservableCollection<GroupDayEntry> Groups { get; set; } = [];

    public RowDayEntry? FindRow(Guid rowId, FuelType fuel) =>
        Rows.FirstOrDefault(e => e.RowId == rowId && e.Fuel == fuel);

    public RowDayEntry RowEntry(Guid rowId, FuelType fuel)
    {
        var entry = FindRow(rowId, fuel);
        if (entry is null)
        {
            entry = new RowDayEntry { RowId = rowId, Fuel = fuel };
            Rows.Add(entry);
        }
        return entry;
    }

    public GroupDayEntry? FindGroup(Guid groupId, FuelType fuel) =>
        Groups.FirstOrDefault(e => e.GroupId == groupId && e.Fuel == fuel);

    public GroupDayEntry GroupEntry(Guid groupId, FuelType fuel)
    {
        var entry = FindGroup(groupId, fuel);
        if (entry is null)
        {
            entry = new GroupDayEntry { GroupId = groupId, Fuel = fuel };
            Groups.Add(entry);
        }
        return entry;
    }
}

/// <summary>Данные строки за сутки по одному виду топлива.</summary>
public sealed partial class RowDayEntry : ObservableObject
{
    public Guid RowId { get; set; }
    public FuelType Fuel { get; set; }

    /// <summary>Потери, кг (вручную).</summary>
    [ObservableProperty]
    public partial double LossKg { get; set; }

    /// <summary>Подвоз: получено складом, кг.</summary>
    [ObservableProperty]
    public partial double ReceivedKg { get; set; }

    /// <summary>Выполнена дозаправка ВВСТ подразделения со склада части.</summary>
    [ObservableProperty]
    public partial bool Refuel { get; set; }

    /// <summary>Количество дозаправки, кг; null — до установленных запасов.</summary>
    [ObservableProperty]
    public partial double? RefuelKg { get; set; }
}

/// <summary>Данные воинской части за сутки по одному виду топлива.</summary>
public sealed partial class GroupDayEntry : ObservableObject
{
    public Guid GroupId { get; set; }
    public FuelType Fuel { get; set; }

    /// <summary>Топливо загружено в транспорт подвоза (иначе — на складе).</summary>
    [ObservableProperty]
    public partial bool InTransport { get; set; }
}
