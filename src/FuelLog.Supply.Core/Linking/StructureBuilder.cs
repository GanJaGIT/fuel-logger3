using System.Collections.ObjectModel;
using FuelLog.Core.Model;
using FuelLog.Supply.Core.Model;

namespace FuelLog.Supply.Core.Linking;

public sealed record SyncResult(int GroupsCreated, int RowsCreated, int RowsLinked)
{
    public bool IsEmpty => GroupsCreated == 0 && RowsCreated == 0 && RowsLinked == 0;
}

/// <summary>
/// Типовая структура рд (по приказу МО РФ №074) и сопоставление со структурой проектов модели 1.
/// </summary>
public static class StructureBuilder
{
    public const string HeadquartersName = "управление";

    /// <summary>Типовой транспорт подвоза (марки и вместимость цистерн).</summary>
    public static IEnumerable<TransportModel> DefaultTransportCatalog() =>
    [
        new() { Name = "АТЗ-7-5350 (КамАЗ-5350)", Fuel = FuelType.Diesel, CapacityLiters = 7000 },
        new() { Name = "АЦ-7-4310 (КамАЗ-4310)", Fuel = FuelType.Diesel, CapacityLiters = 7000 },
        new() { Name = "АЦ-5,5-4310 (КамАЗ-4310)", Fuel = FuelType.Diesel, CapacityLiters = 5500 },
        new() { Name = "ТЗА-7,5-5335 (МАЗ-5335)", Fuel = FuelType.Diesel, CapacityLiters = 7500 },
        new() { Name = "АЦ-8,7-5320 (КамАЗ-5320)", Fuel = FuelType.Diesel, CapacityLiters = 8700 },
        new() { Name = "АЦ-10-4320 (Урал-4320)", Fuel = FuelType.Diesel, CapacityLiters = 10000 },
        new() { Name = "АЦ-9-5320 (КамАЗ-4320)", Fuel = FuelType.Diesel, CapacityLiters = 9000 },
        new() { Name = "АТЗ-9,0-4320 (Урал-4320)", Fuel = FuelType.Diesel, CapacityLiters = 9000 },
        new() { Name = "АТЗ-10-4320 (Урал-4320)", Fuel = FuelType.Diesel, CapacityLiters = 10000 },
        new() { Name = "АТЗ-8-4320 (Урал-4320)", Fuel = FuelType.Diesel, CapacityLiters = 8000 },
        new() { Name = "АМЗ-7,0-5557 (Урал-5557)", Fuel = FuelType.Diesel, CapacityLiters = 7000 },
        new() { Name = "АТМЗ-5,5-4310 (КамАЗ-4310)", Fuel = FuelType.Petrol, CapacityLiters = 5500 },
    ];

    private static SupplyRow Unit(string name, double refills) =>
        new() { Name = name, Kind = RowKind.Unit, Norm = SupplyRow.OwnNorm(refills) };

    private static SupplyRow Warehouse(string name, double coefficient, NormScope scope) =>
        new()
        {
            Name = name,
            Kind = RowKind.Warehouse,
            Norm = coefficient > 0 ? [new NormTerm { Coefficient = coefficient, Scope = scope }] : [],
        };

    /// <summary>Подразделение подвоза: 1 × своя техника + доля техники части (рд).</summary>
    private static SupplyRow Supply(string name, double share, NormScope scope) =>
        new()
        {
            Name = name,
            Kind = RowKind.Unit,
            Norm = [new NormTerm { Coefficient = 1, Scope = NormScope.Own }, new NormTerm { Coefficient = share, Scope = scope }],
        };

    /// <summary>Управление рд: склад рд = 3,1 × Мз рд; БМТО = 1 × Мз БМТО + 0,4 × Мз рд.</summary>
    public static SupplyGroup Headquarters()
    {
        var warehouse = Warehouse("Склад рд", 3.1, NormScope.Division);
        var bmto = Supply("БМТО", 0.4, NormScope.Division);
        var group = new SupplyGroup { Name = HeadquartersName, WarehouseRowId = warehouse.Id, TransportRowId = bmto.Id };
        group.Rows.Add(warehouse);
        group.Rows.Add(bmto);
        foreach (var name in new[] { "ББО", "БОР", "БСБУС", "ПКП", "СФПС", "ПК", "ОМедБ" })
            group.Rows.Add(Unit(name, 1));
        return group;
    }

    /// <summary>Ракетный полк: склад рд = 2,3 × Мз рп; РМТО = 1 × Мз РМТО + 0,4 × Мз рп.</summary>
    public static SupplyGroup Regiment(string name)
    {
        var division = Warehouse("Склад рд", 2.3, NormScope.Group);
        var warehouse = Warehouse("Склад рп", 0, NormScope.Group);
        var rmto = Supply("РМТО", 0.4, NormScope.Group);
        var group = new SupplyGroup { Name = name, WarehouseRowId = warehouse.Id, TransportRowId = rmto.Id };
        group.Rows.Add(division);
        group.Rows.Add(warehouse);
        group.Rows.Add(Unit("1рдн", 1.4));
        group.Rows.Add(Unit("2рдн", 1.4));
        group.Rows.Add(Unit("3рдн", 1.4));
        group.Rows.Add(Unit("ДБО", 1));
        group.Rows.Add(Unit("ДБУ", 1));
        group.Rows.Add(rmto);
        foreach (var unit in new[] { "ГИО", "ЭРО", "ПК", "МедП" })
            group.Rows.Add(Unit(unit, 1));
        return group;
    }

    /// <summary>Техническая ракетная база: склад рд = 2,3 × Мз трб; склад трб = 0,5 × Мз трб; рмто = 1 × Мз рмто + 0,3 × Мз трб.</summary>
    public static SupplyGroup TechnicalBase(string name)
    {
        var division = Warehouse("Склад рд", 2.3, NormScope.Group);
        var warehouse = Warehouse("Склад трб", 0.5, NormScope.Group);
        var rmto = Supply("рмто", 0.3, NormScope.Group);
        var group = new SupplyGroup { Name = name, WarehouseRowId = warehouse.Id, TransportRowId = rmto.Id };
        group.Rows.Add(division);
        group.Rows.Add(warehouse);
        group.Rows.Add(rmto);
        group.Rows.Add(Unit("1ГИР ПУ", 1));
        group.Rows.Add(Unit("2ГИР ПУ", 1));
        return group;
    }

    /// <summary>Шаблон части по названию: управление, рп или трб; для прочих — только склад части.</summary>
    public static SupplyGroup ForName(string name)
    {
        var key = Normalize(name);
        if (key.StartsWith("упр", StringComparison.Ordinal))
        {
            var hq = Headquarters();
            hq.Name = name;
            return hq;
        }
        if (key.Contains("трб", StringComparison.Ordinal))
            return TechnicalBase(name);
        if (key.Contains("рп", StringComparison.Ordinal))
            return Regiment(name);

        var warehouse = Warehouse($"Склад {name}", 0, NormScope.Group);
        var group = new SupplyGroup { Name = name, WarehouseRowId = warehouse.Id };
        group.Rows.Add(warehouse);
        return group;
    }

    /// <summary>Новый проект с типовой структурой: управление, N рп, трб — и типовым транспортом.</summary>
    public static SupplyProject CreateTypical(string name, int regiments = 4, bool technicalBase = true)
    {
        var project = new SupplyProject { Name = name };
        foreach (var model in DefaultTransportCatalog())
            project.TransportCatalog.Add(model);
        var hq = Headquarters();
        project.Groups.Add(hq);
        project.MainWarehouseRowId = hq.WarehouseRowId;
        for (var i = 1; i <= regiments; i++)
            project.Groups.Add(Regiment($"{i}рп"));
        if (technicalBase)
            project.Groups.Add(TechnicalBase("трб"));
        return project;
    }

    /// <summary>
    /// Сопоставляет структуру с проектами модели 1 по названиям частей и подразделений
    /// (без учёта регистра и пробелов): недостающие части создаются по шаблону,
    /// подразделения привязываются, новые подразделения добавляются строками «1 заправка своей техники».
    /// Уже привязанные строки не меняются.
    /// </summary>
    public static SyncResult Sync(SupplyProject project, Model1Context model1)
    {
        int groupsCreated = 0, rowsCreated = 0, rowsLinked = 0;

        foreach (var model in model1.Models.Where(m => m.Project is not null))
        {
            foreach (var group1 in model.Project!.Groups)
            {
                var group = project.Groups.FirstOrDefault(g => Normalize(g.Name) == Normalize(group1.Name));
                if (group is null)
                {
                    group = ForName(group1.Name);
                    project.Groups.Add(group);
                    groupsCreated++;
                    if (project.MainWarehouseRowId is null && Normalize(group.Name).StartsWith("упр", StringComparison.Ordinal))
                        project.MainWarehouseRowId = group.WarehouseRowId;
                }

                foreach (var unit in group1.Units)
                {
                    if (group.Rows.Any(r => r.LinkProjectId == model.Link.Id && r.LinkUnitId == unit.Id))
                        continue;

                    var row = group.Rows.FirstOrDefault(r => !r.IsLinked && r.Kind == RowKind.Unit && Normalize(r.Name) == Normalize(unit.Name));
                    if (row is null)
                    {
                        row = Unit(unit.Name, 1);
                        group.Rows.Add(row);
                        rowsCreated++;
                    }
                    else
                    {
                        rowsLinked++;
                    }
                    row.LinkProjectId = model.Link.Id;
                    row.LinkUnitId = unit.Id;
                }
            }
        }
        return new SyncResult(groupsCreated, rowsCreated, rowsLinked);
    }

    /// <summary>Снимает привязки к удалённому проекту модели 1.</summary>
    public static void Unlink(SupplyProject project, LinkedProject link)
    {
        foreach (var row in project.AllRows.Where(r => r.LinkProjectId == link.Id))
        {
            row.LinkProjectId = null;
            row.LinkUnitId = null;
        }
        project.Links.Remove(link);
    }

    /// <summary>«1 рдн» и «1рдн», «Ё» и «Е» считаются одинаковыми.</summary>
    public static string Normalize(string name) =>
        new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant().Replace('ё', 'е');
}
