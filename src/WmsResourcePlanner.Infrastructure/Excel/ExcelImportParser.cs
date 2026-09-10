using ClosedXML.Excel;
using WmsResourcePlanner.Application.DTOs;

namespace WmsResourcePlanner.Infrastructure.Excel;

/// <summary>
/// Parses the "Resource Plan" sheet of an uploaded workbook into plain
/// ImportRow DTOs (SPEC.md Phase 16). Pure parsing only - no database
/// access; expects the same column layout produced by ExcelExportService
/// (Template | Phase | Workstream | Focus Area | Team | Role | Person |
/// Notes, followed by one column per month with a date-formatted header).
/// </summary>
public static class ExcelImportParser
{
    public static List<ImportRow> ParseResourcePlanSheet(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name, "Resource Plan", StringComparison.OrdinalIgnoreCase));

        if (ws is null)
        {
            throw new InvalidOperationException("The workbook does not contain a \"Resource Plan\" worksheet.");
        }

        var headerRow = ws.Row(1);
        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        // Fixed columns are Template/Phase/Workstream/Focus Area/Team/Role/Person/Notes (1-8);
        // month columns start at 9 and are identified by a parseable date header.
        const int firstMonthColumn = 9;
        var monthColumns = new List<(int Column, int Year, int Month)>();
        for (var c = firstMonthColumn; c <= lastColumn; c++)
        {
            var cell = headerRow.Cell(c);
            DateTime? date = cell.DataType == XLDataType.DateTime
                ? cell.GetDateTime()
                : DateTime.TryParse(cell.GetString(), out var parsed) ? parsed : null;

            if (date is not null)
            {
                monthColumns.Add((c, date.Value.Year, date.Value.Month));
            }
        }

        var rows = new List<ImportRow>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (var r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var team = row.Cell(5).GetString().Trim();
            var role = row.Cell(6).GetString().Trim();

            // A fully blank row (common at the end of a used range) is skipped, not an error.
            if (string.IsNullOrWhiteSpace(team) && string.IsNullOrWhiteSpace(role) &&
                monthColumns.All(mc => row.Cell(mc.Column).IsEmpty()))
            {
                continue;
            }

            var importRow = new ImportRow
            {
                RowNumber = r,
                TemplateName = NullIfBlank(row.Cell(1).GetString()),
                PhaseName = NullIfBlank(row.Cell(2).GetString()),
                WorkstreamName = NullIfBlank(row.Cell(3).GetString()),
                FocusAreaName = NullIfBlank(row.Cell(4).GetString()),
                TeamName = team,
                RoleName = role,
                PersonName = NullIfBlank(row.Cell(7).GetString()),
                Notes = NullIfBlank(row.Cell(8).GetString())
            };

            if (string.IsNullOrWhiteSpace(team)) importRow.Errors.Add("Team is required.");
            if (string.IsNullOrWhiteSpace(role)) importRow.Errors.Add("Role is required.");

            foreach (var (col, year, month) in monthColumns)
            {
                var cell = row.Cell(col);
                if (cell.IsEmpty()) continue;

                if (cell.TryGetValue<decimal>(out var fte))
                {
                    if (fte != 0m)
                    {
                        importRow.MonthlyValues[(year, month)] = fte;
                    }
                }
                else
                {
                    importRow.Errors.Add($"Non-numeric FTE value in {new DateOnly(year, month, 1):MMM yyyy}.");
                }
            }

            rows.Add(importRow);
        }

        return rows;
    }

    /// <summary>
    /// Parses the "Template Plan" sheet into TemplatePlanImportRow DTOs.
    /// Layout matches ExcelExportService.BuildTemplatePlanSheetAsync:
    /// Template | Phase | Start Date | End Date | Notes, followed by one
    /// column per month with an "X" marking that phase's active months.
    /// When a row has any "X" marks, the derived Start/End Date is taken
    /// from the first/last marked month (so adding/removing "X"s adjusts
    /// the phase's dates on re-import); otherwise the explicit Start
    /// Date/End Date cell values are used as-is (so a brand new phase row
    /// can be created by typing dates without using the month grid).
    /// </summary>
    public static List<TemplatePlanImportRow> ParseTemplatePlanSheet(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name, "Template Plan", StringComparison.OrdinalIgnoreCase));

        if (ws is null)
        {
            throw new InvalidOperationException("The workbook does not contain a \"Template Plan\" worksheet.");
        }

        var headerRow = ws.Row(1);
        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        const int firstMonthColumn = 6;
        var monthColumns = new List<(int Column, int Year, int Month)>();
        for (var c = firstMonthColumn; c <= lastColumn; c++)
        {
            var cell = headerRow.Cell(c);
            DateTime? date = cell.DataType == XLDataType.DateTime
                ? cell.GetDateTime()
                : DateTime.TryParse(cell.GetString(), out var parsed) ? parsed : null;

            if (date is not null)
            {
                monthColumns.Add((c, date.Value.Year, date.Value.Month));
            }
        }

        var rows = new List<TemplatePlanImportRow>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (var r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var templateName = row.Cell(1).GetString().Trim();
            var phaseName = row.Cell(2).GetString().Trim();

            if (string.IsNullOrWhiteSpace(templateName) && string.IsNullOrWhiteSpace(phaseName) &&
                monthColumns.All(mc => row.Cell(mc.Column).IsEmpty()) &&
                row.Cell(3).IsEmpty() && row.Cell(4).IsEmpty())
            {
                continue;
            }

            var importRow = new TemplatePlanImportRow
            {
                RowNumber = r,
                TemplateName = templateName,
                PhaseName = phaseName,
                Notes = NullIfBlank(row.Cell(5).GetString())
            };

            if (string.IsNullOrWhiteSpace(templateName)) importRow.Errors.Add("Template is required.");
            if (string.IsNullOrWhiteSpace(phaseName)) importRow.Errors.Add("Phase is required.");

            var markedMonths = monthColumns
                .Where(mc => !row.Cell(mc.Column).IsEmpty() &&
                             row.Cell(mc.Column).GetString().Trim().Equals("X", StringComparison.OrdinalIgnoreCase))
                .Select(mc => new DateOnly(mc.Year, mc.Month, 1))
                .OrderBy(d => d)
                .ToList();

            if (markedMonths.Count > 0)
            {
                importRow.StartDate = markedMonths.First();
                importRow.EndDate = markedMonths.Last().AddMonths(1).AddDays(-1);
            }
            else
            {
                var startCell = row.Cell(3);
                var endCell = row.Cell(4);

                DateOnly? start = startCell.DataType == XLDataType.DateTime
                    ? DateOnly.FromDateTime(startCell.GetDateTime())
                    : DateOnly.TryParse(startCell.GetString(), out var parsedStart) ? parsedStart : null;

                DateOnly? end = endCell.DataType == XLDataType.DateTime
                    ? DateOnly.FromDateTime(endCell.GetDateTime())
                    : DateOnly.TryParse(endCell.GetString(), out var parsedEnd) ? parsedEnd : null;

                if (start is null) importRow.Errors.Add("Start Date is required (or mark active months with \"X\").");
                if (end is null) importRow.Errors.Add("End Date is required (or mark active months with \"X\").");

                importRow.StartDate = start;
                importRow.EndDate = end;
            }

            if (importRow.StartDate is not null && importRow.EndDate is not null && importRow.StartDate > importRow.EndDate)
            {
                importRow.Errors.Add("Start Date must not be after End Date.");
            }

            rows.Add(importRow);
        }

        return rows;
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
