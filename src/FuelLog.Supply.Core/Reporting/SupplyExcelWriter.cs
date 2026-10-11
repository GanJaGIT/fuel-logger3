using System.Globalization;
using ClosedXML.Excel;
using FuelLog.Core.Model;
using FuelLog.Supply.Core.Calculation;
using FuelLog.Supply.Core.Model;

namespace FuelLog.Supply.Core.Reporting;

/// <summary>Документ «Потребность и автономность» в Excel: вкладки ДТ и АБ по форме + лист операций суток.</summary>
public static class SupplyExcelWriter
{
    private const string KgFormat = "#,##0";
    private const string RefillFormat = "0.00";
    private const string DaysFormat = "0.0";
    private const int Columns = 17;
    private const int HeaderTop = 3;

    private static readonly XLColor HeaderFill = XLColor.FromArgb(221, 235, 247);
    private static readonly XLColor TotalFill = XLColor.FromArgb(217, 217, 217);
    private static readonly XLColor GrandTotalFill = XLColor.FromArgb(191, 191, 191);
    private static readonly XLColor BlockedFill = XLColor.FromArgb(64, 64, 64);

    public static void Write(SupplyProject project, DayResult diesel, DayResult petrol, string filePath)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = $"Потребность и автономность — {project.Name}";
        workbook.Properties.Author = "FuelLog";

        WriteDocument(workbook.Worksheets.Add("ДТ"), diesel);
        WriteDocument(workbook.Worksheets.Add("АБ"), petrol);
        WriteOperations(workbook.Worksheets.Add("Операции"), project, diesel, petrol);
        workbook.SaveAs(filePath);
    }

    private static void WriteDocument(IXLWorksheet ws, DayResult result)
    {
        ws.Style.Font.FontName = "Times New Roman";
        ws.Style.Font.FontSize = 11;
        ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var fuelName = result.Fuel == FuelType.Diesel ? "ДТ" : "АБ";
        var title = ws.Range(1, 1, 1, Columns);
        title.Merge();
        title.FirstCell().Value = $"Потребность и автономность ({fuelName})";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(2, 1).Value = "Дата: " + result.Day.Date.ToString("d MMMM yyyy 'г.'", CultureInfo.GetCultureInfo("ru-RU"));
        ws.Cell(2, 1).Style.Font.Bold = true;

        WriteHeader(ws);

        var row = HeaderTop + 3;
        foreach (var group in result.Groups)
        {
            var first = row;
            foreach (var line in group.Rows)
            {
                ws.Cell(row, 2).Value = line.Row.Name;
                WriteFigures(ws, row, line.Figures, line.NormRefills, isTotal: false);
                row++;
            }

            ws.Cell(row, 2).Value = "Итого";
            WriteFigures(ws, row, group.Totals, null, isTotal: true);
            var totalRange = ws.Range(row, 2, row, Columns);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Fill.BackgroundColor = TotalFill;
            ws.Cell(row, 4).Style.Fill.BackgroundColor = BlockedFill;

            var groupCell = ws.Range(first, 1, row, 1);
            groupCell.Merge();
            groupCell.FirstCell().Value = group.Group.Name;
            groupCell.Style.Font.Bold = true;
            groupCell.Style.Font.Italic = true;
            groupCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            groupCell.Style.Alignment.WrapText = true;
            row++;
        }

        if (result.Groups.Count > 1)
        {
            var grand = ws.Range(row, 1, row, 2);
            grand.Merge();
            grand.FirstCell().Value = "Итого по рд";
            WriteFigures(ws, row, result.Total, null, isTotal: true);
            var range = ws.Range(row, 1, row, Columns);
            range.Style.Font.Bold = true;
            range.Style.Fill.BackgroundColor = GrandTotalFill;
            ws.Cell(row, 4).Style.Fill.BackgroundColor = BlockedFill;
            row++;
        }

        var table = ws.Range(HeaderTop, 1, Math.Max(row - 1, HeaderTop + 2), Columns);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Range(HeaderTop + 3, 3, Math.Max(row - 1, HeaderTop + 3), Columns).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Column(1).Width = 14;
        ws.Column(2).Width = 16;
        ws.Columns(3, Columns).Width = 10;
        ws.SheetView.FreezeRows(HeaderTop + 2);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(HeaderTop, HeaderTop + 2);
        ws.PageSetup.Margins.SetLeft(0.3).SetRight(0.3).SetTop(0.4).SetBottom(0.4);
    }

    private static void WriteHeader(IXLWorksheet ws)
    {
        var top = HeaderTop;
        void Merge(int r1, int c1, int r2, int c2, string text)
        {
            var range = ws.Range(r1, c1, r2, c2);
            range.Merge();
            range.FirstCell().Value = text;
        }

        Merge(top, 1, top + 1, 1, "Воинская часть");
        Merge(top, 2, top + 1, 2, "Подразделение");
        string[] titles = ["Установленные запасы (З)", "Остаток (О)", "Приход (Пр)", "Расход (R)", "Потери (Р)", "Наличие (Н)", "Потребность (П)"];
        for (var i = 0; i < titles.Length; i++)
        {
            var col = 3 + i * 2;
            Merge(top, col, top, col + 1, titles[i]);
            ws.Cell(top + 1, col).Value = "кг";
            ws.Cell(top + 1, col + 1).Value = "запр";
        }
        ws.Cell(top, 17).Value = "Автономность (А)";
        ws.Cell(top + 1, 17).Value = "сут";
        for (var c = 1; c <= Columns; c++)
            ws.Cell(top + 2, c).Value = c;

        var header = ws.Range(top, 1, top + 2, Columns);
        header.Style.Font.Bold = true;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.WrapText = true;
        header.Style.Fill.BackgroundColor = HeaderFill;
        ws.Row(top).Height = 30;
        ws.Range(top + 2, 1, top + 2, Columns).Style.Font.Italic = true;
    }

    private static void WriteFigures(IXLWorksheet ws, int row, Figures f, double? normRefills, bool isTotal)
    {
        void Pair(int col, double kg, double? refills)
        {
            ws.Cell(row, col).Value = kg;
            ws.Cell(row, col).Style.NumberFormat.Format = KgFormat;
            if (refills is { } r)
            {
                ws.Cell(row, col + 1).Value = r;
                ws.Cell(row, col + 1).Style.NumberFormat.Format = RefillFormat;
            }
        }

        Pair(3, f.NormKg, isTotal ? null : normRefills);
        Pair(5, f.OpeningKg, f.Refills(f.OpeningKg));
        Pair(7, f.IncomeKg, f.Refills(f.IncomeKg));
        Pair(9, f.ExpenseKg, f.Refills(f.ExpenseKg));
        Pair(11, f.LossKg, f.Refills(f.LossKg));
        Pair(13, f.BalanceKg, f.Refills(f.BalanceKg));
        Pair(15, f.NeedKg, f.Refills(f.NeedKg));
        if (f.AutonomyDays is { } days)
        {
            ws.Cell(row, 17).Value = days;
            ws.Cell(row, 17).Style.NumberFormat.Format = DaysFormat;
        }
        else
        {
            ws.Cell(row, 17).Value = "—";
        }
    }

    private static void WriteOperations(IXLWorksheet ws, SupplyProject project, DayResult diesel, DayResult petrol)
    {
        string[] headers =
        [
            "Топливо", "Воинская часть", "Подразделение",
            "Расход на БЗ (модель 1), кг", "Дозаправка: получено, кг", "Дозаправка: выдано, кг",
            "Подвоз: получено, кг", "Подвоз: выдано, кг", "Транспорт: принято, кг", "Транспорт: передано, кг", "Потери, кг",
        ];
        ws.Cell(1, 1).Value = $"Операции за {diesel.Day.Date:dd.MM.yyyy} — {project.Name}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(3, i + 1).Value = headers[i];

        var row = 4;
        foreach (var result in new[] { diesel, petrol })
        {
            foreach (var line in result.AllRows)
            {
                double[] values =
                [
                    line.Expense.Tasks, line.Income.Refuel, line.Expense.RefuelIssued, line.Income.Delivery,
                    line.Expense.DeliveryIssued, line.Income.Transport, line.Expense.TransportIssued, line.Figures.LossKg,
                ];
                if (values.All(v => Math.Abs(v) < 0.0001))
                    continue;
                ws.Cell(row, 1).Value = result.Fuel.ShortName();
                ws.Cell(row, 2).Value = line.Group.Name;
                ws.Cell(row, 3).Value = line.Row.Name;
                for (var i = 0; i < values.Length; i++)
                {
                    if (Math.Abs(values[i]) < 0.0001)
                        continue;
                    ws.Cell(row, 4 + i).Value = values[i];
                    ws.Cell(row, 4 + i).Style.NumberFormat.Format = KgFormat;
                }
                row++;
            }
        }

        var header = ws.Range(3, 1, 3, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Alignment.WrapText = true;
        header.Style.Fill.BackgroundColor = HeaderFill;
        var table = ws.Range(3, 1, Math.Max(row - 1, 3), headers.Length);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Columns(1, 3).Width = 16;
        ws.Columns(4, headers.Length).Width = 14;
        ws.Row(3).Height = 45;

        var warnings = diesel.Warnings.Concat(petrol.Warnings).Distinct().ToList();
        if (warnings.Count > 0)
        {
            row += 1;
            ws.Cell(row++, 1).Value = "Предупреждения";
            ws.Cell(row - 1, 1).Style.Font.Bold = true;
            foreach (var warning in warnings)
                ws.Cell(row++, 1).Value = warning;
        }
    }
}
