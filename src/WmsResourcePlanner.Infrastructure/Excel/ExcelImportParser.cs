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
    /// whose first two cells are "Workstream", "Team" (the Template Plan
    /// table's header row has "Template", "Phase", "Notes" in its first
    /// three cells instead, since it has no Workstream/Team columns).</summary>
    private static int? FindResourcePlanHeaderRow(IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 1; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            if (row.Cell(1).GetString().Trim().Equals("Workstream", StringComparison.OrdinalIgnoreCase) &&
                row.Cell(2).GetString().Trim().Equals("Team", StringComparison.OrdinalIgnoreCase))
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

    // Excel's built-in number format IDs that represent dates (14-22) or date/time (45-47).
    // ClosedXML sometimes fails to classify a reloaded cell as XLDataType.DateTime even when its
    // style uses one of these built-in date format IDs (e.g. 17 = "mmm-yy", the format our own
    // exporter uses for month headers) - the numeric OLE Automation date value is still stored
    // correctly, it's just not tagged as a DateTime type on re-read. To stay robust against this,
    // we also recognize numeric cells whose style is a known date format and interpret their
    // numeric value as an OLE Automation date serial.
    private static readonly HashSet<int> BuiltInDateNumberFormatIds = new() { 14, 15, 16, 17, 18, 19, 20, 21, 22, 45, 46, 47 };

    private static bool LooksLikeDateFormat(IXLCell cell)
    {
        var numberFormatId = cell.Style.NumberFormat.NumberFormatId;
        if (BuiltInDateNumberFormatIds.Contains(numberFormatId))
        {
            return true;
        }

        var format = cell.Style.NumberFormat.Format;
        if (string.IsNullOrEmpty(format))
        {
            return false;
        }

        // Custom date formats (e.g. "mmm-yy") contain date tokens outside quoted literals.
        var inQuotes = false;
        foreach (var ch in format)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && (ch is 'y' or 'm' or 'd'))
            {
                return true;
            }
        }
        return false;
    }

    private static List<(int Column, int Year, int Month)> FindMonthColumns(IXLRow headerRow, int lastColumn)
    {
        var monthColumns = new List<(int Column, int Year, int Month)>();
        for (var c = 1; c <= lastColumn; c++)
        {
            var cell = headerRow.Cell(c);
            DateTime? date = null;

            if (cell.DataType == XLDataType.DateTime)
            {
                date = cell.GetDateTime();
            }
            else if (cell.DataType == XLDataType.Number && LooksLikeDateFormat(cell))
            {
                // Cell survived a save/reload cycle with its date-formatted style intact but lost
                // its DateTime data type classification; the numeric value is still a valid OLE
                // Automation date serial.
                try
                {
                    date = DateTime.FromOADate(cell.GetDouble());
                }
                catch (ArgumentException)
                {
                    date = null;
                }
            }
            else if (DateTime.TryParse(cell.GetString(), out var parsed))
            {
                date = parsed;
            }

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
            ?? throw new InvalidOperationException("Could not find the Resource Plan table's header row (expected \"Workstream\", \"Team\" ... columns) on the \"Resource Plan\" sheet.");

        var headerRow = ws.Row(headerRowNum);
        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        // Fixed columns are Workstream/Team/Pool/Role/Person/Template/Phase/Focus Area/Notes (1-9);
        // month columns are identified by a parseable date header.
        var monthColumns = FindMonthColumns(headerRow, lastColumn);

        var rows = new List<ImportRow>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRowNum;

        for (var r = headerRowNum + 1; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var team = row.Cell(2).GetString().Trim();
            var role = row.Cell(4).GetString().Trim();

            // A fully blank row (common at the end of a used range) is skipped, not an error.
            if (string.IsNullOrWhiteSpace(team) && string.IsNullOrWhiteSpace(role) &&
                monthColumns.All(mc => row.Cell(mc.Column).IsEmpty()))
            {
                continue;
            }

            var personName = NullIfBlank(row.Cell(5).GetString());
            var importRow = new ImportRow
            {
                RowNumber = r,
                WorkstreamName = NullIfBlank(row.Cell(1).GetString()),
                TeamName = team,
                RoleName = role,
                PersonName = personName,
                // The Pool column only applies to open demand rows (blank Person) -
                // when a Person is named, that person's own pool is the effective source.
                PoolName = personName is null ? NullIfBlank(row.Cell(3).GetString()) : null,
                TemplateName = NullIfBlank(row.Cell(6).GetString()),
                PhaseName = NullIfBlank(row.Cell(7).GetString()),
                FocusAreaName = NullIfBlank(row.Cell(8).GetString()),
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

    /// <summary>Finds the starting column of a Reference Data table by its
    /// bold title cell in row 1 (see ExcelExportService.WriteTable). Titles
    /// are matched case-insensitively so a user's minor re-casing doesn't
    /// break parsing.</summary>
    private static int? FindReferenceTableStartColumn(IXLWorksheet ws, string title)
    {
        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var c = 1; c <= lastColumn; c++)
        {
            if (ws.Cell(1, c).GetString().Trim().Equals(title, StringComparison.OrdinalIgnoreCase))
            {
                return c;
            }
        }
        return null;
    }

    private static decimal? TryParseDecimal(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return decimal.TryParse(s.Trim(), out var d) ? d : null;
    }

    /// <summary>
    /// Parses all eight Reference Data master-data tables (People, Teams,
    /// Roles, Templates, Workstreams, Focus Areas, Sites, Resource Pools)
    /// from the "Reference Data" sheet. Each table's start column is
    /// located by its title in row 1 (see ExcelExportService.WriteTable),
    /// so this stays robust to tables being reordered/resized. Column
    /// offsets within a table follow the fixed header order the exporter
    /// writes (Name first, then any extra fields) - a table missing
    /// entirely from the sheet (e.g. an older export) is simply skipped,
    /// yielding an empty list for that entity type rather than an error.
    /// </summary>
    public static ReferenceDataImport ParseReferenceDataSheet(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name, "Reference Data", StringComparison.OrdinalIgnoreCase));

        var result = new ReferenceDataImport();
        if (ws is null)
        {
            // Reference Data is optional - a workbook without one still
            // supports Resource Plan / Template Plan import on their own.
            return result;
        }

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 2;

        var peopleCol = FindReferenceTableStartColumn(ws, "People");
        if (peopleCol is int pc)
        {
            for (var r = 3; r <= lastRow; r++)
            {
                var name = ws.Cell(r, pc).GetString().Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.People.Add(new ReferencePersonRow
                {
                    Name = name,
                    ResourcePoolName = NullIfBlank(ws.Cell(r, pc + 1).GetString()),
                    CapacityFte = TryParseDecimal(ws.Cell(r, pc + 2).GetString())
                });
            }
        }

        var teamsCol = FindReferenceTableStartColumn(ws, "Teams");
        if (teamsCol is int tc)
        {
            for (var r = 3; r <= lastRow; r++)
            {
                var name = ws.Cell(r, tc).GetString().Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Teams.Add(new ReferenceTeamRow
                {
                    Name = name,
                    TeamType = NullIfBlank(ws.Cell(r, tc + 1).GetString())
                });
            }
        }

        var rolesCol = FindReferenceTableStartColumn(ws, "Roles");
        if (rolesCol is int rc)
        {
            for (var r = 3; r <= lastRow; r++)
            {
                var name = ws.Cell(r, rc).GetString().Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Roles.Add(new ReferenceRoleRow
                {
                    Name = name,
                    Category = NullIfBlank(ws.Cell(r, rc + 1).GetString())
                });
            }
        }

        var templatesCol = FindReferenceTableStartColumn(ws, "Templates");
        if (templatesCol is int tpc)
        {
            for (var r = 3; r <= lastRow; r++)
            {
                var name = ws.Cell(r, tpc).GetString().Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Templates.Add(new ReferenceTemplateRow
                {
                    Name = name,
                    Status = NullIfBlank(ws.Cell(r, tpc + 1).GetString())
                });
            }
        }

        var workstreamsCol = FindReferenceTableStartColumn(ws, "Workstreams");
        if (workstreamsCol is int wc)
        {
            for (var r = 3; r <= lastRow; r++)
            {
                var name = ws.Cell(r, wc).GetString().Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Workstreams.Add(new ReferenceWorkstreamRow { Name = name });
            }
        }

        var focusAreasCol = FindReferenceTableStartColumn(ws, "Focus Areas");
        if (focusAreasCol is int fc)
        {
            for (var r = 3; r <= lastRow; r++)
            {
                var name = ws.Cell(r, fc).GetString().Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.FocusAreas.Add(new ReferenceFocusAreaRow { Name = name });
            }
        }

        var sitesCol = FindReferenceTableStartColumn(ws, "Sites");
        if (sitesCol is int sc)
        {
            for (var r = 3; r <= lastRow; r++)
            {
                var name = ws.Cell(r, sc).GetString().Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Sites.Add(new ReferenceSiteRow
                {
                    Name = name,
                    Region = NullIfBlank(ws.Cell(r, sc + 1).GetString())
                });
            }
        }

        var poolsCol = FindReferenceTableStartColumn(ws, "Resource Pools");
        if (poolsCol is int plc)
        {
            for (var r = 3; r <= lastRow; r++)
            {
                var name = ws.Cell(r, plc).GetString().Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.ResourcePools.Add(new ReferenceResourcePoolRow
                {
                    Name = name,
                    Type = NullIfBlank(ws.Cell(r, plc + 1).GetString()),
                    CostCenter = NullIfBlank(ws.Cell(r, plc + 2).GetString()),
                    AverageRate = TryParseDecimal(ws.Cell(r, plc + 3).GetString()),
                    Vendor = NullIfBlank(ws.Cell(r, plc + 4).GetString()),
                    Notes = NullIfBlank(ws.Cell(r, plc + 5).GetString())
                });
            }
        }

        return result;
    }
}
