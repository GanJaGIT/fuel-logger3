using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelLog.App.Services;
using FuelLog.Core.Calculation;
using FuelLog.Core.Reporting;

namespace FuelLog.App.ViewModels;

public enum PreviewRowKind
{
    Line,
    UnitTotal,
    GroupTotal,
    GrandTotal,
}

/// <summary>Строка предпросмотра — один к одному со строкой листа «Расчёт» в Excel.</summary>
public sealed record PreviewRow(PreviewRowKind Kind, string GroupName, string UnitName, string TaskName, ReportFigures Figures)
{
    public bool IsTotal => Kind != PreviewRowKind.Line;
    public string DateLabel => Figures.DateLabel;
}

public sealed record ReportFile(string Path, string Name, DateTime Created, long Size)
{
    public string SizeText => Size < 1024 * 1024 ? $"{Size / 1024.0:0.#} КБ" : $"{Size / 1024.0 / 1024.0:0.#} МБ";
}

/// <summary>Вкладка «Отчёт»: предпросмотр расчёта и выгрузка в Excel.</summary>
public sealed partial class ReportViewModel : ObservableObject
{
    private readonly ProjectSession _session;
    private readonly IDialogService _dialogs;

    public ReportViewModel(ProjectSession session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
    }

    public ObservableCollection<PreviewRow> Rows { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<ReportFile> Files { get; } = [];

    [ObservableProperty]
    public partial bool OnlyIncluded { get; set; } = true;

    [ObservableProperty]
    public partial bool UseDateRange { get; set; }

    [ObservableProperty]
    public partial DateTime? From { get; set; }

    [ObservableProperty]
    public partial DateTime? To { get; set; }

    [ObservableProperty]
    public partial FuelReport? Current { get; private set; }

    [ObservableProperty]
    public partial ReportFile? SelectedFile { get; set; }

    [ObservableProperty]
    public partial string? LastExportPath { get; private set; }

    private bool _suspendRefresh;

    partial void OnOnlyIncludedChanged(bool value) => Refresh();
    partial void OnFromChanged(DateTime? value) => Refresh();
    partial void OnToChanged(DateTime? value) => Refresh();

    partial void OnUseDateRangeChanged(bool value)
    {
        if (value && From is null && To is null && _session.Project.Tasks.Count > 0)
        {
            // По умолчанию период охватывает все задачи проекта.
            _suspendRefresh = true;
            From = _session.Project.Tasks.Min(t => t.Date).ToDateTime(TimeOnly.MinValue);
            To = _session.Project.Tasks.Max(t => t.Date).ToDateTime(TimeOnly.MinValue);
            _suspendRefresh = false;
        }
        Refresh();
    }

    private ReportOptions Options => new()
    {
        OnlyIncluded = OnlyIncluded,
        From = UseDateRange && From is { } f ? DateOnly.FromDateTime(f) : null,
        To = UseDateRange && To is { } t ? DateOnly.FromDateTime(t) : null,
    };

    [RelayCommand]
    public void Refresh()
    {
        if (_suspendRefresh)
            return;

        var report = FuelReport.Build(_session.Project, Options);
        Current = report;

        Rows.Clear();
        foreach (var group in report.Groups)
        {
            foreach (var unit in group.Units)
            {
                foreach (var line in unit.Rows)
                    Rows.Add(new PreviewRow(PreviewRowKind.Line, line.Group.Name, line.Unit.Name, line.Task.Name, line.Figures));
                Rows.Add(new PreviewRow(PreviewRowKind.UnitTotal, $"Итого {unit.Unit.Name} {group.Group.Name}", string.Empty, string.Empty, unit.Figures));
            }
            Rows.Add(new PreviewRow(PreviewRowKind.GroupTotal, $"Итого {group.Group.Name}", string.Empty, string.Empty, group.Figures));
        }
        if (report.Groups.Count > 1)
            Rows.Add(new PreviewRow(PreviewRowKind.GrandTotal, "Итого", string.Empty, string.Empty, report.Total));

        Warnings.Clear();
        foreach (var warning in report.Warnings)
            Warnings.Add(warning);

        RefreshFiles();
    }

    [RelayCommand]
    private void Export()
    {
        var report = FuelReport.Build(_session.Project, Options);
        if (report.IsEmpty)
        {
            _dialogs.Info("Нет данных для отчёта: отметьте задачи «в расчёт» и добавьте в них технику.");
            return;
        }

        if (report.Warnings.Count > 0 && !_dialogs.Confirm(
                $"При расчёте есть предупреждения ({report.Warnings.Count}):\n\n• " +
                string.Join("\n• ", report.Warnings.Take(5)) +
                (report.Warnings.Count > 5 ? "\n…" : string.Empty) +
                "\n\nВсё равно сформировать отчёт?"))
            return;

        try
        {
            Directory.CreateDirectory(_session.ReportsFolder);
            var path = Path.Combine(_session.ReportsFolder, $"Расчёт ГСМ {DateTime.Now:yyyy-MM-dd HH-mm-ss}.xlsx");
            ExcelReportWriter.Write(report, path);
            LastExportPath = path;
            RefreshFiles();
            _dialogs.OpenWithShell(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Не удалось сохранить отчёт: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenFile(ReportFile? file)
    {
        if ((file ?? SelectedFile) is { } f)
            _dialogs.OpenWithShell(f.Path);
    }

    [RelayCommand]
    private void ShowFile(ReportFile? file)
    {
        if ((file ?? SelectedFile) is { } f)
            _dialogs.ShowInExplorer(f.Path);
    }

    [RelayCommand]
    private void ShowLastExport()
    {
        if (LastExportPath is not null)
            _dialogs.ShowInExplorer(LastExportPath);
    }

    [RelayCommand]
    private void OpenReportsFolder()
    {
        Directory.CreateDirectory(_session.ReportsFolder);
        _dialogs.ShowInExplorer(_session.ReportsFolder);
    }

    [RelayCommand]
    private void DeleteFile(ReportFile? file)
    {
        if ((file ?? SelectedFile) is not { } f || !_dialogs.Confirm($"Удалить файл отчёта «{f.Name}»?"))
            return;
        try
        {
            File.Delete(f.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Не удалось удалить файл (возможно, он открыт в Excel): {ex.Message}");
        }
        RefreshFiles();
    }

    private void RefreshFiles()
    {
        Files.Clear();
        var folder = new DirectoryInfo(_session.ReportsFolder);
        if (!folder.Exists)
            return;
        foreach (var file in folder.GetFiles("*.xlsx").Where(f => !f.Name.StartsWith("~$")).OrderByDescending(f => f.LastWriteTime))
            Files.Add(new ReportFile(file.FullName, file.Name, file.LastWriteTime, file.Length));
    }
}
