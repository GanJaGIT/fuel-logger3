using ClosedXML.Excel;
using FuelLog.Core.Calculation;
using FuelLog.Core.Model;

namespace FuelLog.Core.Reporting;

/// <summary>Выгрузка отчёта в Excel по форме образца «Расчёты образец.xlsx».</summary>
public static class ExcelReportWriter
{
    private const string KgFormat = "#,##0.00";
    private const string RefillFormat = "0.00";
    private const string MassFormat = "#,##0";
    private const string DateFormat = "dd.mm.yyyy";
    private const int HeaderRows = 5;
    private const int Columns = 12;

    private static readonly XLColor UnitTotalFill = XLColor.FromArgb(242, 242, 242);
    private static readonly XLColor GroupTotalFill = XLColor.FromArgb(217, 217, 217);
    private static readonly XLColor GrandTotalFill = XLColor.FromArgb(191, 191, 191);
    private static readonly XLColor HeaderFill = XLColor.FromArgb(221, 235, 247);

    public static void Write(FuelReport report, string filePath)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = $"Расчёт расхода ГСМ — {report.ProjectName}";
        workbook.Properties.Author = "FuelLog";
        workbook.Properties.Created = report.GeneratedAt;

        WriteSummarySheet(workbook.Worksheets.Add("Расчёт"), report);
        WriteDetailsSheet(workbook.Worksheets.Add("Детализация"), report);
        WriteParametersSheet(workbook.Worksheets.Add("Параметры"), report);

        workbook.SaveAs(filePath);
    }

    private static void WriteSummarySheet(IXLWorksheet ws, FuelReport report)
    {
        ws.Style.Font.FontName = "Times New Roman";
        ws.Style.Font.FontSize = 12;
        ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        WriteSummaryHeader(ws);

        var row = HeaderRows + 1;
        foreach (var group in report.Groups)
        {
            foreach (var unit in group.Units)
            {
                foreach (var line in unit.Rows)
                {
                    ws.Cell(row, 1).Value = line.Group.Name;
                    ws.Cell(row, 2).Value = line.Unit.Name;
                    ws.Cell(row, 3).Value = line.Task.Name;
                    ws.Cell(row, 4).Value = line.Task.Date.ToDateTime(TimeOnly.MinValue);
                    ws.Cell(row, 4).Style.DateFormat.Format = DateFormat;
                    WriteFigures(ws, row, line.Figures);
                    row++;
                }

                WriteTotalRow(ws, row++, $"Итого {unit.Unit.Name} {group.Group.Name}", unit.Figures, UnitTotalFill);
            }

            WriteTotalRow(ws, row++, $"Итого {group.Group.Name}", group.Figures, GroupTotalFill);
        }

        if (report.Groups.Count > 1)
            WriteTotalRow(ws, row++, "Итого", report.Total, GrandTotalFill);

        var lastRow = Math.Max(row - 1, HeaderRows);
        var table = ws.Range(1, 1, lastRow, Columns);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Range(HeaderRows + 1, 4, lastRow, Columns).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(HeaderRows + 1, 1, lastRow, 3).Style.Alignment.WrapText = true;

        ws.Column(1).Width = 20;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 36;
        ws.Column(4).Width = 13;
        ws.Columns(5, 8).Width = 11;
        ws.Columns(9, 12).Width = 12;

        ws.SheetView.FreezeRows(HeaderRows);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(1, HeaderRows);
        ws.PageSetup.Margins.SetLeft(0.4).SetRight(0.4).SetTop(0.5).SetBottom(0.5);
    }

    private static void WriteSummaryHeader(IXLWorksheet ws)
    {
        void Header(string range, string text)
        {
            var r = ws.Range(range);
            r.Merge();
            r.FirstCell().Value = text;
        }

        Header("A1:A5", "Воинская часть (рп)");
        Header("B1:B5", "Подразделение");
        Header("C1:C5", "Выполняемая задача");
        Header("D1:D5", "Дата выполнения задачи");
        Header("E1:H3", "Расход");
        Header("I1:J3", "Масса заправки (кг)");
        Header("K1:L3", "Среднесуточный расход (запр)");
        Header("E4:F4", "АБ");
        Header("G4:H4", "ДТ");
        Header("I4:I5", "АБ");
        Header("J4:J5", "ДТ");
        Header("K4:K5", "АБ");
        Header("L4:L5", "ДТ");
        ws.Cell("E5").Value = "кг";
        ws.Cell("F5").Value = "запр";
        ws.Cell("G5").Value = "кг";
        ws.Cell("H5").Value = "запр";

        var header = ws.Range(1, 1, HeaderRows, Columns);
        header.Style.Font.Bold = true;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Alignment.WrapText = true;
        header.Style.Fill.BackgroundColor = HeaderFill;
    }

    private static void WriteFigures(IXLWorksheet ws, int row, ReportFigures f)
    {
        SetNumber(ws.Cell(row, 5), f.Kg.Petrol, KgFormat);
        SetNumber(ws.Cell(row, 6), f.Refills.Petrol, RefillFormat);
        SetNumber(ws.Cell(row, 7), f.Kg.Diesel, KgFormat);
        SetNumber(ws.Cell(row, 8), f.Refills.Diesel, RefillFormat);
        SetNumber(ws.Cell(row, 9), f.Mass.Petrol, MassFormat);
        SetNumber(ws.Cell(row, 10), f.Mass.Diesel, MassFormat);
        SetNumber(ws.Cell(row, 11), f.DailyRefills.Petrol, RefillFormat);
        SetNumber(ws.Cell(row, 12), f.DailyRefills.Diesel, RefillFormat);
    }

    private static void WriteTotalRow(IXLWorksheet ws, int row, string title, ReportFigures figures, XLColor fill)
    {
        var titleRange = ws.Range(row, 1, row, 3);
        titleRange.Merge();
        titleRange.FirstCell().Value = title;

        if (figures.Dates.Count == 1)
        {
            ws.Cell(row, 4).Value = figures.Dates[0].ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 4).Style.DateFormat.Format = DateFormat;
        }
        else
        {
            ws.Cell(row, 4).Value = figures.DateLabel;
        }

        WriteFigures(ws, row, figures);

        var range = ws.Range(row, 1, row, Columns);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = fill;
    }

    private static void SetNumber(IXLCell cell, double value, string format)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = format;
    }

    private static void WriteDetailsSheet(IXLWorksheet ws, FuelReport report)
    {
        string[] headers =
        [
            "Дата", "Задача", "Тип задачи", "Воинская часть (рп)", "Подразделение", "Марка", "Топливо",
            "Кол-во", "Потеряно", "Марш, км", "Работа, ч", "Норма, л/100 км", "Норма, л/ч",
            "Марш, л", "Работа, л", "Потери (баки), л", "Всего, л", "Всего, кг",
        ];

        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        var row = 2;
        foreach (var d in report.Details)
        {
            ws.Cell(row, 1).Value = d.Task.Date.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 1).Style.DateFormat.Format = DateFormat;
            ws.Cell(row, 2).Value = d.Task.Name;
            ws.Cell(row, 3).Value = d.Task.TaskType;
            ws.Cell(row, 4).Value = d.Group.Name;
            ws.Cell(row, 5).Value = d.Unit.Name;
            ws.Cell(row, 6).Value = d.Model.Name;
            ws.Cell(row, 7).Value = d.Calc.Fuel.ShortName();
            ws.Cell(row, 8).Value = d.Calc.Count;
            ws.Cell(row, 9).Value = d.Calc.LostCount;
            ws.Cell(row, 10).Value = d.Item.DistanceKm;
            ws.Cell(row, 11).Value = d.Item.WorkHours;
            ws.Cell(row, 12).Value = d.Model.LitersPer100Km;
            ws.Cell(row, 13).Value = d.Model.LitersPerHour;
            SetNumber(ws.Cell(row, 14), d.Calc.MarchLiters, KgFormat);
            SetNumber(ws.Cell(row, 15), d.Calc.WorkLiters, KgFormat);
            SetNumber(ws.Cell(row, 16), d.Calc.LossLiters, KgFormat);
            SetNumber(ws.Cell(row, 17), d.Calc.TotalLiters, KgFormat);
            SetNumber(ws.Cell(row, 18), d.Calc.TotalKg, KgFormat);
            row++;
        }

        var header = ws.Range(1, 1, 1, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = HeaderFill;
        header.Style.Alignment.WrapText = true;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var used = ws.Range(1, 1, Math.Max(row - 1, 1), headers.Length);
        used.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        used.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        if (row > 2)
            used.SetAutoFilter();

        ws.Columns(1, headers.Length).AdjustToContents(1, Math.Min(row, 500), 8, 40);
        ws.Row(1).Height = 32;
        ws.SheetView.FreezeRows(1);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
    }

    private static void WriteParametersSheet(IXLWorksheet ws, FuelReport report)
    {
        var rows = new (string Name, XLCellValue Value)[]
        {
            ("Проект", report.ProjectName),
            ("Сформирован", report.GeneratedAt.ToString("dd.MM.yyyy HH:mm")),
            ("Задач в расчёте", report.TaskCount),
            ("Плотность АБ, кг/л", report.PetrolDensity),
            ("Плотность ДТ, кг/л", report.DieselDensity),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 1, 1).Value = rows[i].Name;
            ws.Cell(i + 1, 2).Value = rows[i].Value;
        }

        var next = rows.Length + 2;
        ws.Cell(next, 1).Style.Font.Bold = true;
        ws.Cell(next++, 1).Value = "Методика";
        string[] method =
        [
            "Потери, ед. = кол-во × % потерь (округление до целого).",
            "Расход, л = работающие × (км / 100 × норма л/100 км + ч × норма л/ч) + потерянные × (бак осн. + бак доп.).",
            "Расход, кг = расход, л × плотность.",
            "Масса заправки = Σ (баки × кол-во × плотность) по всей технике подразделения (рп, итога).",
            "Заправки = расход, кг / масса заправки соответствующего уровня.",
            "Среднесуточный расход = заправки / число дней выполнения задач.",
        ];
        foreach (var line in method)
            ws.Cell(next++, 1).Value = line;

        if (report.Warnings.Count > 0)
        {
            next++;
            ws.Cell(next, 1).Style.Font.Bold = true;
            ws.Cell(next++, 1).Value = "Предупреждения";
            foreach (var warning in report.Warnings)
                ws.Cell(next++, 1).Value = warning;
        }

        ws.Range(1, 1, rows.Length, 1).Style.Font.Bold = true;
        ws.Column(1).Width = 24;
        ws.Column(2).Width = 30;
    }
}
