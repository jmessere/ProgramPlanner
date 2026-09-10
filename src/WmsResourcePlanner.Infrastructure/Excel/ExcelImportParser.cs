using ClosedXML.Excel;
using WmsResourcePlanner.Application.DTOs;

namespace WmsResourcePlanner.Infrastructure.Excel;

/// <summary>
/// Parses the "Resource Plan" sheet of an uploaded workbook into plain
/// ImportRow DTOs (SPEC.md Phase 16). Pure parsing only - no database
/// access; expects the same column layout produced by ExcelExportService
/// (Template | Phase | Workstream | Focus Area | Team | Role | Person |
/// Pool | Notes, followed by one column per month with a date-formatted
/// header).
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

        // Fixed columns are Template/Phase/Workstream/Focus Area/Team/Role/Person/Pool/Notes (1-9);
        // month columns start at 10 and are identified by a parseable date header.
        const int firstMonthColumn = 10;
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

            var personName = NullIfBlank(row.Cell(7).GetString());
            var importRow = new ImportRow
            {
                RowNumber = r,
                TemplateName = NullIfBlank(row.Cell(1).GetString()),
                PhaseName = NullIfBlank(row.Cell(2).GetString()),
                WorkstreamName = NullIfBlank(row.Cell(3).GetString()),
                FocusAreaName = NullIfBlank(row.Cell(4).GetString()),
                TeamName = team,
                RoleName = role,
                PersonName = personName,
                // The Pool column only applies to open demand rows (blank Person) -
                // when a Person is named, that person's own pool is the effective source.
                PoolName = personName is null ? NullIfBlank(row.Cell(8).GetString()) : null,
                Notes = NullIfBlank(row.Cell(9).GetString())
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
    /// Template | Phase | Notes, followed by one column per month with an
    /// "X" marking that phase's active months. The "X" marks are the sole
    /// source of truth for a phase's dates - there are no separate Start
    /// Date/End Date columns that could disagree with them - so the
    /// derived StartDate/EndDate always come from the first/last marked
    /// month. A row with no "X" marks at all is an error (a new phase
    /// must have at least one active month marked).
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

        const int firstMonthColumn = 4;
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
                monthColumns.All(mc => row.Cell(mc.Column).IsEmpty()))
            {
                continue;
            }

            var importRow = new TemplatePlanImportRow
            {
                RowNumber = r,
                TemplateName = templateName,
                PhaseName = phaseName,
                Notes = NullIfBlank(row.Cell(3).GetString())
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
                importRow.Errors.Add("At least one month must be marked with \"X\" to set this phase's active date range.");
            }

            rows.Add(importRow);
        }

        return rows;
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
