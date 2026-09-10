using ClosedXML.Excel;
using WmsResourcePlanner.Application.DTOs;

namespace WmsResourcePlanner.Infrastructure.Excel;

/// <summary>
/// Parses the "Resource Plan" sheet of an uploaded workbook into plain
/// ImportRow / TemplatePlanImportRow DTOs (SPEC.md Phase 16). Pure parsing
/// only - no database access. Since ExcelExportService now writes both the
/// Template Plan table and the Resource Plan table onto the single
/// "Resource Plan" sheet (Template Plan first, Resource Plan below,
/// sharing the same month columns), both parsers below locate their own
/// header row within that one sheet rather than assuming fixed row
/// numbers, so a user's manual row insertions/deletions before either
/// table don't break parsing.
/// </summary>
public static class ExcelImportParser
{
    /// <summary>Finds the header row of the Resource Plan table: the row
    /// whose first three cells are "Template", "Phase", "Workstream" (the
    /// Template Plan table's header row has "Notes" in the third cell
    /// instead, since it has no Workstream column).</summary>
    private static int? FindResourcePlanHeaderRow(IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 1; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            if (row.Cell(1).GetString().Trim().Equals("Template", StringComparison.OrdinalIgnoreCase) &&
                row.Cell(2).GetString().Trim().Equals("Phase", StringComparison.OrdinalIgnoreCase) &&
                row.Cell(3).GetString().Trim().Equals("Workstream", StringComparison.OrdinalIgnoreCase))
            {
                return r;
            }
        }
        return null;
    }

    /// <summary>Finds the header row of the Template Plan table (see
    /// FindResourcePlanHeaderRow for how the two header rows are told
    /// apart).</summary>
    private static int? FindTemplatePlanHeaderRow(IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 1; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            if (row.Cell(1).GetString().Trim().Equals("Template", StringComparison.OrdinalIgnoreCase) &&
                row.Cell(2).GetString().Trim().Equals("Phase", StringComparison.OrdinalIgnoreCase) &&
                row.Cell(3).GetString().Trim().Equals("Notes", StringComparison.OrdinalIgnoreCase))
            {
                return r;
            }
        }
        return null;
    }

    private static List<(int Column, int Year, int Month)> FindMonthColumns(IXLRow headerRow, int lastColumn)
    {
        var monthColumns = new List<(int Column, int Year, int Month)>();
        for (var c = 1; c <= lastColumn; c++)
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
        return monthColumns;
    }

    public static List<ImportRow> ParseResourcePlanSheet(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name, "Resource Plan", StringComparison.OrdinalIgnoreCase));

        if (ws is null)
        {
            throw new InvalidOperationException("The workbook does not contain a \"Resource Plan\" worksheet.");
        }

        var headerRowNum = FindResourcePlanHeaderRow(ws)
            ?? throw new InvalidOperationException("Could not find the Resource Plan table's header row (expected \"Template\", \"Phase\", \"Workstream\" ... columns) on the \"Resource Plan\" sheet.");

        var headerRow = ws.Row(headerRowNum);
        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        // Fixed columns are Template/Phase/Workstream/Focus Area/Team/Role/Person/Pool/Notes (1-9);
        // month columns are identified by a parseable date header.
        var monthColumns = FindMonthColumns(headerRow, lastColumn);

        var rows = new List<ImportRow>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRowNum;

        for (var r = headerRowNum + 1; r <= lastRow; r++)
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
    /// Parses the Template Plan table from the "Resource Plan" sheet into
    /// TemplatePlanImportRow DTOs. Layout matches
    /// ExcelExportService.BuildResourcePlanSheetAsync: Template | Phase |
    /// Notes, followed by one column per month (aligned with the Resource
    /// Plan table's month columns below it) with an "X" marking that
    /// phase's active months. The "X" marks are the sole source of truth
    /// for a phase's dates - there are no separate Start Date/End Date
    /// columns that could disagree with them - so the derived
    /// StartDate/EndDate always come from the first/last marked month. A
    /// row with no "X" marks at all is an error (a new phase must have at
    /// least one active month marked).
    /// </summary>
    public static List<TemplatePlanImportRow> ParseTemplatePlanSheet(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name, "Resource Plan", StringComparison.OrdinalIgnoreCase));

        if (ws is null)
        {
            throw new InvalidOperationException("The workbook does not contain a \"Resource Plan\" worksheet.");
        }

        var templateHeaderRowNum = FindTemplatePlanHeaderRow(ws)
            ?? throw new InvalidOperationException("Could not find the Template Plan table's header row (expected \"Template\", \"Phase\", \"Notes\" columns) on the \"Resource Plan\" sheet.");

        // The Resource Plan table's header row (if present) bounds where
        // the Template Plan table's data rows end, since both tables now
        // share one sheet.
        var resourcePlanHeaderRowNum = FindResourcePlanHeaderRow(ws);

        var headerRow = ws.Row(templateHeaderRowNum);
        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var monthColumns = FindMonthColumns(headerRow, lastColumn);

        var rows = new List<TemplatePlanImportRow>();
        var lastRow = resourcePlanHeaderRowNum is int rpRow && rpRow > templateHeaderRowNum
            ? rpRow - 1
            : ws.LastRowUsed()?.RowNumber() ?? templateHeaderRowNum;

        for (var r = templateHeaderRowNum + 1; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var templateName = row.Cell(1).GetString().Trim();
            var phaseName = row.Cell(2).GetString().Trim();

            if (string.IsNullOrWhiteSpace(phaseName) &&
                monthColumns.All(mc => row.Cell(mc.Column).IsEmpty()))
            {
                // Either a fully blank row, or the "Resource Plan" section
                // title row that follows this table on the shared sheet
                // (which only has a value in column 1) - neither is real
                // Template Plan data.
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
