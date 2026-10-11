using System.Windows.Threading;
using FuelLog.Core.Model;
using FuelLog.Supply.Core.Calculation;
using FuelLog.Supply.Core.Linking;
using FuelLog.Supply.Core.Model;
using FuelLog.Supply.Core.Storage;

namespace FuelLog.Supply.Services;

/// <summary>
/// Открытый документ модели 2: автосохранение, данные привязанных проектов модели 1 и пересчёт.
/// После любой правки документ пересчитывается (с небольшой задержкой, чтобы объединить серию правок).
/// </summary>
public sealed class SupplySession : DocumentSession<SupplyProject>
{
    private readonly DispatcherTimer _recalc;

    public SupplySession(SupplyProject project, string filePath)
        : base(project, filePath, SupplyStore.Save)
    {
        Model1 = Model1Context.Load(project, filePath);
        _recalc = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _recalc.Tick += (_, _) =>
        {
            _recalc.Stop();
            Recalculate();
        };
        Changed += (_, _) =>
        {
            _recalc.Stop();
            _recalc.Start();
        };
        Recalculate();
    }

    public SupplyProject Project => Document;
    public string ReportsFolder => SupplyStore.ReportsFolder(FilePath);

    public Model1Context Model1 { get; private set; }

    public IReadOnlyList<DayResult> Diesel { get; private set; } = [];
    public IReadOnlyList<DayResult> Petrol { get; private set; } = [];

    /// <summary>Документ пересчитан — экраны обновляют показатели.</summary>
    public event EventHandler? Recalculated;

    public IReadOnlyList<DayResult> Results(FuelType fuel) => fuel == FuelType.Diesel ? Diesel : Petrol;

    public DayResult? Result(SupplyDay? day, FuelType fuel) =>
        day is null ? null : Results(fuel).FirstOrDefault(r => ReferenceEquals(r.Day, day));

    /// <summary>Перечитывает изменившиеся проекты модели 1 и пересчитывает документ.</summary>
    public void RefreshModel1(bool force = false)
    {
        if (force)
            Model1 = Model1Context.Load(Project, FilePath);
        else if (Model1.IsStale(Project, FilePath))
            Model1 = Model1.Refresh(Project, FilePath);
        else
            return;
        Recalculate();
    }

    public void Recalculate()
    {
        if (Model1.IsStale(Project, FilePath))
            Model1 = Model1.Refresh(Project, FilePath);
        Diesel = SupplyCalculator.Compute(Project, Model1, FuelType.Diesel);
        Petrol = SupplyCalculator.Compute(Project, Model1, FuelType.Petrol);
        Recalculated?.Invoke(this, EventArgs.Empty);
    }
}
