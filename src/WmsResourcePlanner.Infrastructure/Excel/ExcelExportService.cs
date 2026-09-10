using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Application.Interfaces;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Infrastructure.Excel;

/// <summary>
/// Produces a standalone, professionally formatted resource-planning
/// workbook (no application/technical identifiers visible to the reader).
/// Reuses ResourceTransformationService so Excel and the in-app grid share
/// identical monthly projection logic.
/// </summary>
public class ExcelExportService
{
    private readonly IAppDbContext _db;
    private readonly ResourceTransformationService _engine;

    public ExcelExportService(IAppDbContext db, ResourceTransformationService engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<byte[]> ExportAsync(int scenarioId, DateOnly horizonStart, DateOnly horizonEnd, CancellationToken ct = default)
    {
        var lines = await _db.ResourcePlanLines
            .Where(r => r.ScenarioId == scenarioId)
            .Include(r => r.Team)
            .Include(r => r.Role)
            .Include(r => r.Person)
            .Include(r => r.Workstream)
            .Include(r => r.FocusArea)
            .Include(r => r.TeamTemplateAssignment).ThenInclude(a => a!.Template)
            .Include(r => r.TemplatePhase).ThenInclude(p => p!.Template)
            .ToListAsync(ct);

        var monthly = _engine.ExpandToMonthly(lines, horizonStart, horizonEnd);
        var sample = lines.GroupBy(l => l.ToRowKey()).ToDictionary(g => g.Key, g => g.First());

        var months = new List<DateOnly>();
        var cursor = new DateOnly(horizonStart.Year, horizonStart.Month, 1);
        var end = new DateOnly(horizonEnd.Year, horizonEnd.Month, 1);
        while (cursor <= end)
        {
            months.Add(cursor);
            cursor = cursor.AddMonths(1);
        }

        using var workbook = new XLWorkbook();

        BuildInstructionsSheet(workbook);
        BuildResourcePlanSheet(workbook, monthly, sample, months);
        await BuildTemplatePlanSheetAsync(workbook, months, ct);
        await BuildReferenceDataSheetAsync(workbook, ct);
        BuildSummarySheet(workbook, months, lines);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildInstructionsSheet(XLWorkbook workbook)
    {
        var ws = workbook.Worksheets.Add("Instructions");
        ws.Cell(1, 1).Value = "Resource Plan Workbook";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16);
        ws.Cell(3, 1).Value = "This workbook contains the current resource plan across a multi-year timeline.";
        ws.Cell(4, 1).Value = "Resource Plan: one row per planning line, filterable via the column header dropdowns. Monthly columns hold FTE (0.25 = quarter FTE, 1.0 = one FTE).";
        ws.Cell(5, 1).Value = "A populated Person means a named allocation. A blank Person means open (unfilled) demand.";
        ws.Cell(6, 1).Value = "Template Plan: template phases with their date ranges, filterable, and a monthly timeline where an \"X\" marks the phase's active months (colored like a Gantt bar). Team assignments are not shown here - they are implied by the Team values already present on the Resource Plan sheet.";
        ws.Cell(7, 1).Value = "Reference Data: master lists (people, teams, roles, templates, workstreams, etc.).";
        ws.Cell(8, 1).Value = "Summary: filled/open FTE by month, gaps by role/team/template, and overallocated people.";
        ws.Columns().AdjustToContents();
    }

    private void BuildResourcePlanSheet(
        XLWorkbook workbook,
        Dictionary<Application.DTOs.ResourcePlanRowKey, List<Application.DTOs.MonthlyValue>> monthly,
        Dictionary<Application.DTOs.ResourcePlanRowKey, ResourcePlanLine> sample,
        List<DateOnly> months)
    {
        var ws = workbook.Worksheets.Add("Resource Plan");

        string[] fixedHeaders = { "Template", "Phase", "Workstream", "Focus Area", "Team", "Role", "Person", "Notes" };
        for (var i = 0; i < fixedHeaders.Length; i++)
        {
            ws.Cell(1, i + 1).Value = fixedHeaders[i];
        }

        var firstMonthCol = fixedHeaders.Length + 1;
        for (var i = 0; i < months.Count; i++)
        {
            var cell = ws.Cell(1, firstMonthCol + i);
            cell.Value = months[i].ToDateTime(TimeOnly.MinValue);
            cell.Style.DateFormat.Format = "mmm-yy";
            cell.Style.Font.SetBold();
        }

        ws.Row(1).Style.Font.SetBold();
        ws.SheetView.FreezeRows(1);
        ws.SheetView.FreezeColumns(fixedHeaders.Length);

        var row = 2;
        foreach (var (key, values) in monthly.OrderBy(m => sample[m.Key].Team?.Name).ThenBy(m => sample[m.Key].Role?.Name))
        {
            var s = sample[key];
            var templateName = s.TeamTemplateAssignment?.Template?.Name ?? s.TemplatePhase?.Template?.Name;

            ws.Cell(row, 1).Value = templateName;
            ws.Cell(row, 2).Value = s.TemplatePhase?.Name;
            ws.Cell(row, 3).Value = s.Workstream?.Name;
            ws.Cell(row, 4).Value = s.FocusArea?.Name;
            ws.Cell(row, 5).Value = s.Team?.Name;
            ws.Cell(row, 6).Value = s.Role?.Name;
            ws.Cell(row, 7).Value = s.Person?.DisplayName;
            ws.Cell(row, 8).Value = s.Notes;

            for (var i = 0; i < values.Count; i++)
            {
                if (values[i].Fte != 0m)
                {
                    ws.Cell(row, firstMonthCol + i).Value = values[i].Fte;
                }
            }

            row++;
        }

        ws.Columns(1, fixedHeaders.Length).AdjustToContents();

        // AutoFilter (rather than a structured Excel Table/ListObject) gives
        // every column - including the date-valued month headers - the
        // standard filter-dropdown UX the user asked for, without the
        // restrictions a ListObject would impose here (unique text-only
        // headers, no coexisting frozen-pane quirks, etc.).
        if (row > 2)
        {
            ws.Range(1, 1, row - 1, firstMonthCol + months.Count - 1).SetAutoFilter();
        }
    }

    private async Task BuildTemplatePlanSheetAsync(XLWorkbook workbook, List<DateOnly> months, CancellationToken ct)
    {
        var ws = workbook.Worksheets.Add("Template Plan");

        // Team assignments are intentionally not shown here (per product
        // direction): a Team's association with a Template is already
        // implied by the Team values on the Resource Plan sheet, so this
        // sheet only needs to show Templates and their Phases. Start/End
        // Date columns are intentionally omitted too: the monthly "X"
        // marks are the single source of truth for a phase's dates (both
        // on export and on re-import), so showing separate date columns
        // that could disagree with the X's would be confusing.
        string[] headers = { "Template", "Phase", "Notes" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

        var firstMonthCol = headers.Length + 1;
        for (var i = 0; i < months.Count; i++)
        {
            var cell = ws.Cell(1, firstMonthCol + i);
            cell.Value = months[i].ToDateTime(TimeOnly.MinValue);
            cell.Style.DateFormat.Format = "mmm-yy";
        }

        ws.Row(1).Style.Font.SetBold();
        ws.SheetView.FreezeRows(1);
        ws.SheetView.FreezeColumns(headers.Length);

        var row = 2;

        var phases = await _db.TemplatePhases.Include(p => p.Template).OrderBy(p => p.Template!.Name).ThenBy(p => p.SortOrder).ToListAsync(ct);
        foreach (var p in phases)
        {
            ws.Cell(row, 1).Value = p.Template?.Name;
            ws.Cell(row, 2).Value = p.Name;
            ws.Cell(row, 3).Value = p.Notes;

            var lastXCol = -1;
            for (var i = 0; i < months.Count; i++)
            {
                if (months[i] >= new DateOnly(p.StartDate.Year, p.StartDate.Month, 1) &&
                    months[i] <= new DateOnly(p.EndDate.Year, p.EndDate.Month, 1))
                {
                    ws.Cell(row, firstMonthCol + i).Value = "X";
                    lastXCol = firstMonthCol + i;
                }
            }

            // Conditional formatting (not a one-time fill) so that if a user
            // manually adds/removes "X" marks in this row before re-importing,
            // the visual Gantt-bar-like coloring stays in sync automatically.
            // Each phase gets the same color it's shown with everywhere else
            // in the app (NameColorPalette, keyed by phase name).
            if (lastXCol >= firstMonthCol)
            {
                var color = XLColor.FromHtml(Application.Services.NameColorPalette.ColorFor(p.Name));
                ws.Range(row, firstMonthCol, row, firstMonthCol + months.Count - 1)
                    .AddConditionalFormat()
                    .WhenEquals("X")
                    .Fill.SetBackgroundColor(color)
                    .Font.SetFontColor(XLColor.White);
            }

            row++;
        }

        ws.Columns(1, headers.Length).AdjustToContents();

        if (row > 2)
        {
            ws.Range(1, 1, row - 1, firstMonthCol + months.Count - 1).SetAutoFilter();
        }
    }


    private async Task BuildReferenceDataSheetAsync(XLWorkbook workbook, CancellationToken ct)
    {
        var ws = workbook.Worksheets.Add("Reference Data");
        var col = 1;

        col = WriteTable(ws, col, "People", (await _db.People.OrderBy(p => p.DisplayName).ToListAsync(ct))
            .Select(p => new[] { p.DisplayName, p.EmployeeType, p.DefaultCapacityFte.ToString("0.##") }), new[] { "Name", "Employee Type", "Capacity FTE" });

        col = WriteTable(ws, col, "Teams", (await _db.Teams.OrderBy(t => t.Name).ToListAsync(ct))
            .Select(t => new[] { t.Name, t.TeamType }), new[] { "Name", "Team Type" });

        col = WriteTable(ws, col, "Roles", (await _db.Roles.OrderBy(r => r.Name).ToListAsync(ct))
            .Select(r => new[] { r.Name, r.Category ?? string.Empty }), new[] { "Name", "Category" });

        col = WriteTable(ws, col, "Templates", (await _db.Templates.OrderBy(t => t.SortOrder).ToListAsync(ct))
            .Select(t => new[] { t.Name, t.Status.ToString() }), new[] { "Name", "Status" });

        col = WriteTable(ws, col, "Workstreams", (await _db.Workstreams.OrderBy(w => w.Name).ToListAsync(ct))
            .Select(w => new[] { w.Name }), new[] { "Name" });

        col = WriteTable(ws, col, "Focus Areas", (await _db.FocusAreas.OrderBy(f => f.Name).ToListAsync(ct))
            .Select(f => new[] { f.Name }), new[] { "Name" });

        col = WriteTable(ws, col, "Sites", (await _db.Sites.OrderBy(s => s.Name).ToListAsync(ct))
            .Select(s => new[] { s.Name, s.Region ?? string.Empty }), new[] { "Name", "Region" });

        ws.Columns().AdjustToContents();
    }

    private static int WriteTable(IXLWorksheet ws, int startCol, string title, IEnumerable<string[]> rows, string[] headers)
    {
        ws.Cell(1, startCol).Value = title;
        ws.Cell(1, startCol).Style.Font.SetBold().Font.SetFontSize(12);
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(2, startCol + i).Value = headers[i];
            ws.Cell(2, startCol + i).Style.Font.SetBold();
        }

        var row = 3;
        foreach (var r in rows)
        {
            for (var i = 0; i < r.Length; i++)
            {
                ws.Cell(row, startCol + i).Value = r[i];
            }
            row++;
        }

        return startCol + headers.Length + 1;
    }

    // Resource Plan sheet's fixed column layout (kept in sync with
    // BuildResourcePlanSheet above) - used to build live formula
    // references from the Summary sheet instead of baking in static
    // snapshot values, so the Summary recalculates if a user edits the
    // Resource Plan sheet directly in Excel.
    private const int RpTeamCol = 5;
    private const int RpRoleCol = 6;
    private const int RpPersonCol = 7;
    private const int RpFirstMonthCol = 9;

    // A generous fixed row bound (rather than a true whole-column
    // reference) so header-row cells - which hold text like "Person" or a
    // month date serial - are never swept into the sums below.
    private const int RpMaxDataRow = 100000;

    private void BuildSummarySheet(XLWorkbook workbook, List<DateOnly> months, List<ResourcePlanLine> lines)
    {
        var ws = workbook.Worksheets.Add("Summary");
        var rpWs = workbook.Worksheet("Resource Plan");

        string ColLetter(int col) => rpWs.Cell(1, col).Address.ColumnLetter;
        string RpRange(int col) => $"'Resource Plan'!${ColLetter(col)}$2:${ColLetter(col)}${RpMaxDataRow}";

        ws.Cell(1, 1).Value = "Filled FTE by Month";
        ws.Cell(1, 1).Style.Font.SetBold();
        ws.Cell(2, 1).Value = "Month";
        ws.Cell(2, 2).Value = "Filled FTE";
        ws.Cell(2, 3).Value = "Open FTE";
        ws.Cell(2, 4).Value = "Total Need";
        ws.Row(2).Style.Font.SetBold();

        var personRange = RpRange(RpPersonCol);

        var row = 3;
        foreach (var m in months)
        {
            var monthCol = RpFirstMonthCol + months.IndexOf(m);
            var monthRange = RpRange(monthCol);

            ws.Cell(row, 1).Value = m.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 1).Style.DateFormat.Format = "mmm-yy";
            ws.Cell(row, 2).FormulaA1 = $"=SUMIFS({monthRange},{personRange},\"<>\")";
            ws.Cell(row, 3).FormulaA1 = $"=SUMIFS({monthRange},{personRange},\"\")";
            ws.Cell(row, 4).FormulaA1 = $"=B{row}+C{row}";
            row++;
        }

        // Helper "Row Total FTE" column at the end of the Resource Plan
        // sheet: one formula per data row summing that row's months, so the
        // Role/Team breakdowns below can SUMIFS a single column instead of
        // re-summing every month column per group.
        var rowTotalCol = RpFirstMonthCol + months.Count;
        rpWs.Cell(1, rowTotalCol).Value = "Row Total FTE";
        rpWs.Cell(1, rowTotalCol).Style.Font.SetBold();
        var rpLastRow = 1 + lines.Select(l => l.ToRowKey()).Distinct().Count();
        for (var r = 2; r <= rpLastRow; r++)
        {
            rpWs.Cell(r, rowTotalCol).FormulaA1 =
                $"=SUM({ColLetter(RpFirstMonthCol)}{r}:{ColLetter(RpFirstMonthCol + months.Count - 1)}{r})";
        }
        var rowTotalRange = RpRange(rowTotalCol);

        row += 2;
        ws.Cell(row, 1).Value = "Open FTE by Role (current)";
        ws.Cell(row, 1).Style.Font.SetBold();
        row++;
        foreach (var roleName in lines.Where(l => l.PersonId == null).Select(l => l.Role?.Name ?? "(unspecified)").Distinct().OrderBy(n => n))
        {
            ws.Cell(row, 1).Value = roleName;
            ws.Cell(row, 2).FormulaA1 = $"=SUMIFS({rowTotalRange},{RpRange(RpRoleCol)},A{row},{personRange},\"\")";
            row++;
        }

        row += 1;
        ws.Cell(row, 1).Value = "Open FTE by Team (current)";
        ws.Cell(row, 1).Style.Font.SetBold();
        row++;
        foreach (var teamName in lines.Where(l => l.PersonId == null).Select(l => l.Team?.Name ?? "(unspecified)").Distinct().OrderBy(n => n))
        {
            ws.Cell(row, 1).Value = teamName;
            ws.Cell(row, 2).FormulaA1 = $"=SUMIFS({rowTotalRange},{RpRange(RpTeamCol)},A{row},{personRange},\"\")";
            row++;
        }

        ws.Columns().AdjustToContents();
    }
}

