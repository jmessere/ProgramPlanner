using ClosedXML.Excel;
using WmsResourcePlanner.Application.DTOs;

namespace WmsResourcePlanner.Infrastructure.Excel;

/// <summary>
/// Parses the "Resource Plan" sheet of an uploaded workbook into plain
/// ImportRow DTOs (SPEC.md Phase 16). Pure parsing only - no database
/// access; expects the same column layout produced by ExcelExportService
/// (Template | Phase | Workstream | Team | Role | Person | Notes, followed
/// by one column per month with a date-formatted header).
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

        // Fixed columns are Template/Phase/Workstream/Team/Role/Person/Notes (1-7);
        // month columns start at 8 and are identified by a parseable date header.
        const int firstMonthColumn = 8;
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
            var team = row.Cell(4).GetString().Trim();
            var role = row.Cell(5).GetString().Trim();

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
                TeamName = team,
                RoleName = role,
                PersonName = NullIfBlank(row.Cell(6).GetString()),
                Notes = NullIfBlank(row.Cell(7).GetString())
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

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
