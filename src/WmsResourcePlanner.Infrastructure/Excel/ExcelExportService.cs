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
            .Include(r => r.ResourcePool)
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

        var pools = await _db.ResourcePools.OrderBy(p => p.Name).ToListAsync(ct);

        using var workbook = new XLWorkbook();

        BuildInstructionsSheet(workbook);
        var rpLayout = await BuildResourcePlanSheetAsync(workbook, monthly, sample, months, ct);
        var refCols = await BuildReferenceDataSheetAsync(workbook, pools, ct);
        BuildSummarySheet(workbook, months, lines, pools, refCols, rpLayout);

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
        ws.Cell(4, 1).Value = "Resource Plan: a single sheet with two tables sharing the same month columns so they line up for at-a-glance planning. The Template Plan table (top) shows each template phase's active months as an \"X\" (colored like a Gantt bar). The Resource Plan table (below) is the row-per-planning-line grid, filterable via the column header dropdowns, with monthly FTE (0.25 = quarter FTE, 1.0 = one FTE). Both tables' month columns are frozen alongside the Resource Plan header row, so the template overlay and the row headers stay visible while you scroll through plan data.";
        ws.Cell(5, 1).Value = "A populated Person means a named allocation. A blank Person means open (unfilled) demand; its Pool column names the proposed sourcing pool.";
        ws.Cell(6, 1).Value = "Template Plan table: template phases with a monthly timeline where an \"X\" marks the phase's active months; the X marks are the sole source of truth for a phase's dates. Team assignments are not shown here - they are implied by the Team values already present in the Resource Plan table below.";
        ws.Cell(7, 1).Value = "Reference Data: master lists (people, teams, roles, templates, workstreams, etc.).";
        ws.Cell(8, 1).Value = "Summary: filled/open FTE by month, gaps by role/team/template, overallocated people, and Pool/Vendor cost & headcount rollups by quarter and year (all live formulas).";
        ws.Columns().AdjustToContents();
    }

    /// <summary>Row positions of the Resource Plan table within the merged
    /// "Resource Plan" sheet (which now also holds the Template Plan table
    /// above it), captured while building that sheet so the Summary sheet
    /// can build live formula ranges against the right rows instead of
    /// assuming the Resource Plan table starts at row 1/2.</summary>
    private readonly record struct ResourcePlanSheetLayout(int HeaderRow, int FirstDataRow, int LastDataRow);

    /// <summary>
    /// Builds the single "Resource Plan" sheet: the Template Plan table
    /// first (template phases with an "X" marking each phase's active
    /// months), then the Resource Plan table (one row per planning line)
    /// directly below it. Both tables share the same month columns (fixed
    /// headers end at the same column, months start right after) so a
    /// planner can see the template overlay lined up with the plan data
    /// while entering/reviewing FTE. Team assignments are not repeated on
    /// the Template Plan table - they are already implied by the Team
    /// values in the Resource Plan table below.
    /// </summary>
    private async Task<ResourcePlanSheetLayout> BuildResourcePlanSheetAsync(
        XLWorkbook workbook,
        Dictionary<Application.DTOs.ResourcePlanRowKey, List<Application.DTOs.MonthlyValue>> monthly,
        Dictionary<Application.DTOs.ResourcePlanRowKey, ResourcePlanLine> sample,
        List<DateOnly> months,
        CancellationToken ct)
    {
        var ws = workbook.Worksheets.Add("Resource Plan");

        string[] fixedHeaders = { "Template", "Phase", "Workstream", "Focus Area", "Team", "Role", "Person", "Pool", "Notes" };
        var firstMonthCol = RpFirstMonthCol; // shared by both tables so months align

        void WriteMonthHeaders(int headerRow)
        {
            for (var i = 0; i < months.Count; i++)
            {
                var cell = ws.Cell(headerRow, firstMonthCol + i);
                cell.Value = months[i].ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "mmm-yy";
                cell.Style.Font.SetBold();
            }
        }

        // ---- Template Plan table (top) ----
        var row = 1;
        ws.Cell(row, 1).Value = "Template Plan";
        ws.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(13);
        row++;

        ws.Cell(row, 1).Value = "Template";
        ws.Cell(row, 2).Value = "Phase";
        ws.Cell(row, 3).Value = "Notes";
        WriteMonthHeaders(row);
        ws.Row(row).Style.Font.SetBold();
        var templateHeaderRow = row;
        row++;

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

        var templateLastRow = row - 1;

        row += 2; // blank separator rows between the two tables

        // ---- Resource Plan table (below, aligned month columns) ----
        ws.Cell(row, 1).Value = "Resource Plan";
        ws.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(13);
        row++;

        for (var i = 0; i < fixedHeaders.Length; i++)
        {
            ws.Cell(row, i + 1).Value = fixedHeaders[i];
        }
        WriteMonthHeaders(row);
        ws.Row(row).Style.Font.SetBold();
        var resourcePlanHeaderRow = row;
        row++;

        var firstDataRow = row;
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
            // Pool only shown for open demand rows - once a Person is named,
            // their own Resource Pool is the implicit source (see Reference
            // Data > People for that mapping) so showing it here too could
            // disagree with the Person's actual pool after re-import.
            ws.Cell(row, 8).Value = s.Person is null ? s.ResourcePool?.Name : null;
            ws.Cell(row, 9).Value = s.Notes;

            for (var i = 0; i < values.Count; i++)
            {
                if (values[i].Fte != 0m)
                {
                    ws.Cell(row, firstMonthCol + i).Value = values[i].Fte;
                }
            }

            row++;
        }

        var lastDataRow = row - 1;

        ws.Columns(1, fixedHeaders.Length).AdjustToContents();

        // Freeze through the Resource Plan header row (not just row 1) so
        // the Template Plan overlay above stays visible while scrolling
        // through Resource Plan data - this is what gives the planner the
        // "template overlay while planning" view the two tables share.
        ws.SheetView.FreezeRows(resourcePlanHeaderRow);
        ws.SheetView.FreezeColumns(fixedHeaders.Length);

        // Structured Excel Tables (ListObjects) rather than a single
        // sheet-level AutoFilter, so both the Template Plan overlay and
        // the Resource Plan grid get their own independent filter-dropdown
        // UX even though they share one sheet (Excel only allows one plain
        // AutoFilter range per sheet, but any number of Tables). Each
        // Table only spans the fixed label columns (Template/Phase/Notes
        // for Template Plan; the 9 fixed columns for Resource Plan) and
        // deliberately excludes the month columns: OOXML requires Table
        // column headers to be plain text, but the month headers are
        // Date-typed cells (so they keep their "mmm-yy" formatting and stay
        // parseable by date on re-import) - wrapping them in a Table would
        // silently coerce them away from real dates. This has no filtering
        // downside: an Excel Table's row filter hides/shows entire rows
        // sheet-wide, so filtering by a label column already hides that
        // row's month values too even though those columns sit outside the
        // Table's own range.
        //
        // A Table is only created when at least one real data row exists:
        // ClosedXML's CreateTable() on a header-only range physically
        // inserts a phantom blank data row (shifting every row below it
        // down by one), which would silently corrupt the rest of the
        // sheet's row layout. With zero phases/lines there is nothing to
        // filter anyway, so the headers are simply left as plain text.
        if (templateLastRow > templateHeaderRow)
        {
            ws.Range(templateHeaderRow, 1, templateLastRow, 3).CreateTable("TemplatePlanTable");
        }
        if (lastDataRow >= firstDataRow)
        {
            ws.Range(resourcePlanHeaderRow, 1, lastDataRow, fixedHeaders.Length).CreateTable("ResourcePlanTable");
        }

        return new ResourcePlanSheetLayout(resourcePlanHeaderRow, firstDataRow, Math.Max(lastDataRow, firstDataRow - 1));
    }

    /// <summary>Column positions of key Reference Data tables, captured while
    /// building that sheet so the Summary sheet can build live VLOOKUP
    /// formulas against them instead of hardcoding column numbers that would
    /// silently drift if the reference table order/width ever changes.</summary>
    private readonly record struct ReferenceDataColumns(int PeopleCol, int ResourcePoolsCol);

    private async Task<ReferenceDataColumns> BuildReferenceDataSheetAsync(XLWorkbook workbook, List<ResourcePool> pools, CancellationToken ct)
    {
        var ws = workbook.Worksheets.Add("Reference Data");
        var col = 1;

        var peopleCol = col;
        col = WriteTable(ws, col, "People", (await _db.People.Include(p => p.ResourcePool).OrderBy(p => p.DisplayName).ToListAsync(ct))
            .Select(p => new[] { p.DisplayName, p.ResourcePool?.Name ?? string.Empty, p.DefaultCapacityFte.ToString("0.##") }), new[] { "Name", "Resource Pool", "Capacity FTE" });

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

        var resourcePoolsCol = col;
        col = WriteTable(ws, col, "Resource Pools", pools
            .Select(p => new[] { p.Name, p.Type.ToString(), p.CostCenter ?? string.Empty, p.AverageRate.ToString("0.00"), p.Vendor ?? string.Empty, p.Notes ?? string.Empty }),
            new[] { "Name", "Type", "Cost Center", "Average Rate", "Vendor", "Notes" });

        ws.Columns().AdjustToContents();

        return new ReferenceDataColumns(peopleCol, resourcePoolsCol);
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
    private const int RpPoolCol = 8;
    private const int RpFirstMonthCol = 10;

    // A generous fixed row bound (rather than a true whole-column
    // reference) so header-row cells - which hold text like "Person" or a
    // month date serial - are never swept into the sums below.
    private const int RpMaxDataRow = 100000;

    private void BuildSummarySheet(XLWorkbook workbook, List<DateOnly> months, List<ResourcePlanLine> lines, List<ResourcePool> pools, ReferenceDataColumns refCols, ResourcePlanSheetLayout rpLayout)
    {
        var ws = workbook.Worksheets.Add("Summary");
        var rpWs = workbook.Worksheet("Resource Plan");

        string ColLetter(int col) => rpWs.Cell(rpLayout.HeaderRow, col).Address.ColumnLetter;
        string RpRange(int col) => $"'Resource Plan'!${ColLetter(col)}${rpLayout.FirstDataRow}:${ColLetter(col)}${RpMaxDataRow}";

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
        rpWs.Cell(rpLayout.HeaderRow, rowTotalCol).Value = "Row Total FTE";
        rpWs.Cell(rpLayout.HeaderRow, rowTotalCol).Style.Font.SetBold();
        var rpLastRow = rpLayout.LastDataRow;
        for (var r = rpLayout.FirstDataRow; r <= rpLastRow; r++)
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

        row += 2;
        BuildPoolAndVendorSections(ws, rpWs, ref row, months, lines, pools, refCols, rpLayout.HeaderRow, rpLayout.FirstDataRow, rpLastRow, ColLetter, RpRange, personRange);

        ws.Columns().AdjustToContents();
    }

    // ---------------------------------------------------------------------
    // Pool / Vendor cost & headcount rollups (Summary sheet additions).
    //
    // Every figure here is a live formula, never a snapshot value, so the
    // workbook stays correct if a user edits the Resource Plan sheet
    // directly. To make that possible without repeating rate/quarter logic
    // in every single cell, a handful of hidden helper columns are added to
    // the end of the Resource Plan sheet:
    //   - Effective Pool: the row's own Pool for open demand, or the named
    //     Person's own Resource Pool (looked up from Reference Data) once
    //     a Person is assigned.
    //   - Effective Vendor / Effective Hourly Rate: looked up from the
    //     Resource Pools reference table using the Effective Pool.
    //   - One "<Quarter> Cost" and one "<Quarter> Count" column per
    //     calendar quarter in the horizon: Cost assumes 2080 hours/year
    //     (520/quarter, i.e. 2080/12 per month) times the row's monthly FTE
    //     and hourly rate; Count is simply the row's average FTE across the
    //     quarter's months (so 2 people at 0.5 FTE = headcount of 1).
    // Year figures reuse the quarter helper columns: cost sums the year's
    // quarters, count averages them.
    // ---------------------------------------------------------------------
    private void BuildPoolAndVendorSections(
        IXLWorksheet ws,
        IXLWorksheet rpWs,
        ref int rowRef,
        List<DateOnly> months,
        List<ResourcePlanLine> lines,
        List<ResourcePool> pools,
        ReferenceDataColumns refCols,
        int rpHeaderRow,
        int rpFirstDataRow,
        int rpLastRow,
        Func<int, string> colLetter,
        Func<int, string> rpRange,
        string personRange)
    {
        // row is a ref parameter and can't be captured by the local
        // functions below (SectionTitle/SubTitle/WriteMatrix), so all work
        // happens against this local copy, which is written back at the end.
        var row = rowRef;

        var rowTotalCol = RpFirstMonthCol + months.Count;
        var effPoolCol = rowTotalCol + 1;
        var effVendorCol = rowTotalCol + 2;
        var effRateCol = rowTotalCol + 3;

        rpWs.Cell(rpHeaderRow, effPoolCol).Value = "Effective Pool";
        rpWs.Cell(rpHeaderRow, effVendorCol).Value = "Effective Vendor";
        rpWs.Cell(rpHeaderRow, effRateCol).Value = "Effective Hourly Rate";
        rpWs.Row(rpHeaderRow).Style.Font.SetBold();

        var personColLetter = colLetter(RpPersonCol);
        var poolColLetter = colLetter(RpPoolCol);
        var effPoolColLetter = colLetter(effPoolCol);
        var effRateColLetter = colLetter(effRateCol);

        var peopleRange = $"'Reference Data'!${colLetter(refCols.PeopleCol)}$3:${colLetter(refCols.PeopleCol + 1)}${RpMaxDataRow}";
        var poolsLookupRange = $"'Reference Data'!${colLetter(refCols.ResourcePoolsCol)}$3:${colLetter(refCols.ResourcePoolsCol + 5)}${RpMaxDataRow}";

        for (var r = rpFirstDataRow; r <= rpLastRow; r++)
        {
            rpWs.Cell(r, effPoolCol).FormulaA1 =
                $"=IF(${personColLetter}{r}<>\"\",IFERROR(VLOOKUP(${personColLetter}{r},{peopleRange},2,FALSE),\"\"),${poolColLetter}{r})";
            rpWs.Cell(r, effRateCol).FormulaA1 =
                $"=IF(${effPoolColLetter}{r}<>\"\",IFERROR(VLOOKUP(${effPoolColLetter}{r},{poolsLookupRange},4,FALSE),0),0)";
            rpWs.Cell(r, effVendorCol).FormulaA1 =
                $"=IF(${effPoolColLetter}{r}<>\"\",IFERROR(VLOOKUP(${effPoolColLetter}{r},{poolsLookupRange},5,FALSE),\"\"),\"\")";
        }
        rpWs.Range(rpFirstDataRow, effRateCol, Math.Max(rpFirstDataRow, rpLastRow), effRateCol).Style.NumberFormat.Format = "$#,##0.00";

        var quarters = months
            .Select(m => (Year: m.Year, Quarter: (m.Month - 1) / 3 + 1))
            .Distinct()
            .OrderBy(q => q.Year).ThenBy(q => q.Quarter)
            .ToList();
        var quarterMonthIdx = quarters
            .Select(q => months
                .Select((m, i) => (m, i))
                .Where(x => x.m.Year == q.Year && (x.m.Month - 1) / 3 + 1 == q.Quarter)
                .Select(x => x.i)
                .ToList())
            .ToList();
        var years = months.Select(m => m.Year).Distinct().OrderBy(y => y).ToList();
        var yearQuarterIdx = years
            .Select(y => quarters.Select((q, i) => (q, i)).Where(x => x.q.Year == y).Select(x => x.i).ToList())
            .ToList();

        var quarterCostCol = new int[quarters.Count];
        var quarterCountCol = new int[quarters.Count];
        var nextCol = effRateCol + 1;
        for (var q = 0; q < quarters.Count; q++) quarterCostCol[q] = nextCol++;
        for (var q = 0; q < quarters.Count; q++) quarterCountCol[q] = nextCol++;

        var quarterLabels = quarters.Select(q => $"Q{q.Quarter} {q.Year}").ToList();

        for (var q = 0; q < quarters.Count; q++)
        {
            rpWs.Cell(rpHeaderRow, quarterCostCol[q]).Value = $"{quarterLabels[q]} Cost (helper)";
            rpWs.Cell(rpHeaderRow, quarterCountCol[q]).Value = $"{quarterLabels[q]} Count (helper)";
        }

        for (var r = rpFirstDataRow; r <= rpLastRow; r++)
        {
            for (var q = 0; q < quarters.Count; q++)
            {
                var idxs = quarterMonthIdx[q];
                var monthRange = $"{colLetter(RpFirstMonthCol + idxs[0])}{r}:{colLetter(RpFirstMonthCol + idxs[^1])}{r}";
                rpWs.Cell(r, quarterCostCol[q]).FormulaA1 = $"=SUM({monthRange})*(2080/12)*${effRateColLetter}{r}";
                // SUM/count instead of AVERAGE: a row with no FTE at all in
                // this quarter's months (all blank) would make AVERAGE()
                // return #DIV/0!, which then poisons every pool/vendor
                // SUMIFS that includes this row. SUM treats blanks as 0
                // (correct - "not allocated that month" is 0, not "no
                // data"), and dividing by the fixed month count is
                // mathematically identical to AVERAGE whenever values are
                // actually present.
                rpWs.Cell(r, quarterCountCol[q]).FormulaA1 = $"=SUM({monthRange})/{idxs.Count}";
            }
        }

        if (quarters.Count > 0)
        {
            rpWs.Range(rpFirstDataRow, quarterCostCol[0], Math.Max(rpFirstDataRow, rpLastRow), quarterCostCol[^1]).Style.NumberFormat.Format = "$#,##0";
            rpWs.Range(rpFirstDataRow, quarterCountCol[0], Math.Max(rpFirstDataRow, rpLastRow), quarterCountCol[^1]).Style.NumberFormat.Format = "0.00";
            rpWs.Columns(effPoolCol, quarterCountCol[^1]).Hide();
        }
        else
        {
            rpWs.Columns(effPoolCol, effRateCol).Hide();
        }

        string QCostRange(int q) => rpRange(quarterCostCol[q]);
        string QCountRange(int q) => rpRange(quarterCountCol[q]);
        var effPoolRange = rpRange(effPoolCol);

        string PersonCriteria(string mode) => mode switch
        {
            "Allocated" => $",{personRange},\"<>\"",
            "Demand" => $",{personRange},\"\"",
            _ => string.Empty
        };

        string PoolQuarterFormula(string range, int poolRowNum, string mode) =>
            $"=SUMIFS({range},{effPoolRange},$A{poolRowNum}{PersonCriteria(mode)})";

        string PoolYearFormula(Func<int, string> rangeForQuarter, int poolRowNum, int yearIdx, string mode, bool average)
        {
            var qIdxs = yearQuarterIdx[yearIdx];
            var parts = qIdxs.Select(q => $"SUMIFS({rangeForQuarter(q)},{effPoolRange},$A{poolRowNum}{PersonCriteria(mode)})");
            var sum = string.Join("+", parts);
            return average ? $"=({sum})/{Math.Max(1, qIdxs.Count)}" : $"={sum}";
        }

        void SectionTitle(string text)
        {
            ws.Cell(row, 1).Value = text;
            ws.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(13);
            row++;
        }

        void SubTitle(string text)
        {
            ws.Cell(row, 1).Value = text;
            ws.Cell(row, 1).Style.Font.SetBold().Font.SetItalic();
            row++;
        }

        // Writes one "labels x periods" grid with a Total row at the
        // bottom, and returns the Summary-sheet cell address (e.g.
        // "$C$15") of every data cell so the Vendor tables can sum the
        // exact rows belonging to their member pools out of the matching
        // Pool table, rather than re-deriving totals from scratch.
        string[,] WriteMatrix(List<string> labels, string labelHeader, List<string> periodLabels, Func<int, int, int, string> formula, string numberFormat)
        {
            ws.Cell(row, 1).Value = labelHeader;
            for (var j = 0; j < periodLabels.Count; j++) ws.Cell(row, 2 + j).Value = periodLabels[j];
            ws.Row(row).Style.Font.SetBold();
            row++;

            var addrs = new string[labels.Count, periodLabels.Count];
            var firstDataRow = row;
            for (var i = 0; i < labels.Count; i++)
            {
                ws.Cell(row, 1).Value = labels[i];
                for (var j = 0; j < periodLabels.Count; j++)
                {
                    var cell = ws.Cell(row, 2 + j);
                    cell.FormulaA1 = formula(i, j, row);
                    cell.Style.NumberFormat.Format = numberFormat;
                    addrs[i, j] = $"${cell.Address.ColumnLetter}${row}";
                }
                row++;
            }

            ws.Cell(row, 1).Value = "Total";
            ws.Cell(row, 1).Style.Font.SetBold();
            for (var j = 0; j < periodLabels.Count; j++)
            {
                var cell = ws.Cell(row, 2 + j);
                if (labels.Count > 0)
                {
                    var colLetterForTotal = ws.Cell(firstDataRow, 2 + j).Address.ColumnLetter;
                    cell.FormulaA1 = $"=SUM({colLetterForTotal}{firstDataRow}:{colLetterForTotal}{row - 1})";
                }
                else
                {
                    cell.Value = 0;
                }
                cell.Style.NumberFormat.Format = numberFormat;
            }
            row += 2;

            return addrs;
        }

        var poolNames = pools.Select(p => p.Name).ToList();
        var vendorGroups = pools
            .Where(p => !string.IsNullOrWhiteSpace(p.Vendor))
            .GroupBy(p => p.Vendor!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .ToList();
        var vendorNames = vendorGroups.Select(g => g.Key).ToList();
        var vendorMemberPoolIdx = vendorGroups
            .Select(g => g.Select(p => poolNames.IndexOf(p.Name)).Where(i => i >= 0).ToList())
            .ToList();

        string VendorFormulaFromPool(string[,] poolAddrs, int vendorIdx, int periodIdx)
        {
            var members = vendorMemberPoolIdx[vendorIdx];
            if (members.Count == 0) return "=0";
            return "=" + string.Join("+", members.Select(pi => poolAddrs[pi, periodIdx]));
        }

        var yearLabels = years.Select(y => y.ToString()).ToList();

        SectionTitle("Pool Summary - Cost by Quarter");
        SubTitle("Allocated Only");
        var poolCostQAllocated = WriteMatrix(poolNames, "Pool", quarterLabels, (i, j, r) => PoolQuarterFormula(QCostRange(j), r, "Allocated"), "$#,##0");
        SubTitle("Demand Only");
        var poolCostQDemand = WriteMatrix(poolNames, "Pool", quarterLabels, (i, j, r) => PoolQuarterFormula(QCostRange(j), r, "Demand"), "$#,##0");
        SubTitle("Both (Allocated + Demand)");
        var poolCostQBoth = WriteMatrix(poolNames, "Pool", quarterLabels, (i, j, r) => PoolQuarterFormula(QCostRange(j), r, "Both"), "$#,##0");

        SectionTitle("Pool Summary - Cost by Year");
        SubTitle("Allocated Only");
        var poolCostYAllocated = WriteMatrix(poolNames, "Pool", yearLabels, (i, j, r) => PoolYearFormula(QCostRange, r, j, "Allocated", average: false), "$#,##0");
        SubTitle("Demand Only");
        var poolCostYDemand = WriteMatrix(poolNames, "Pool", yearLabels, (i, j, r) => PoolYearFormula(QCostRange, r, j, "Demand", average: false), "$#,##0");
        SubTitle("Both (Allocated + Demand)");
        var poolCostYBoth = WriteMatrix(poolNames, "Pool", yearLabels, (i, j, r) => PoolYearFormula(QCostRange, r, j, "Both", average: false), "$#,##0");

        SectionTitle("Pool Summary - Count by Quarter");
        SubTitle("Allocated Only");
        WriteMatrix(poolNames, "Pool", quarterLabels, (i, j, r) => PoolQuarterFormula(QCountRange(j), r, "Allocated"), "0.00");
        SubTitle("Demand Only");
        WriteMatrix(poolNames, "Pool", quarterLabels, (i, j, r) => PoolQuarterFormula(QCountRange(j), r, "Demand"), "0.00");
        SubTitle("Both (Allocated + Demand)");
        WriteMatrix(poolNames, "Pool", quarterLabels, (i, j, r) => PoolQuarterFormula(QCountRange(j), r, "Both"), "0.00");

        SectionTitle("Pool Summary - Count by Year");
        SubTitle("Allocated Only");
        WriteMatrix(poolNames, "Pool", yearLabels, (i, j, r) => PoolYearFormula(QCountRange, r, j, "Allocated", average: true), "0.00");
        SubTitle("Demand Only");
        WriteMatrix(poolNames, "Pool", yearLabels, (i, j, r) => PoolYearFormula(QCountRange, r, j, "Demand", average: true), "0.00");
        SubTitle("Both (Allocated + Demand)");
        WriteMatrix(poolNames, "Pool", yearLabels, (i, j, r) => PoolYearFormula(QCountRange, r, j, "Both", average: true), "0.00");

        SectionTitle("Vendor Summary - Cost by Quarter");
        SubTitle("Allocated Only");
        WriteMatrix(vendorNames, "Vendor", quarterLabels, (i, j, r) => VendorFormulaFromPool(poolCostQAllocated, i, j), "$#,##0");
        SubTitle("Demand Only");
        WriteMatrix(vendorNames, "Vendor", quarterLabels, (i, j, r) => VendorFormulaFromPool(poolCostQDemand, i, j), "$#,##0");
        SubTitle("Both (Allocated + Demand)");
        WriteMatrix(vendorNames, "Vendor", quarterLabels, (i, j, r) => VendorFormulaFromPool(poolCostQBoth, i, j), "$#,##0");

        SectionTitle("Vendor Summary - Cost by Year");
        SubTitle("Allocated Only");
        WriteMatrix(vendorNames, "Vendor", yearLabels, (i, j, r) => VendorFormulaFromPool(poolCostYAllocated, i, j), "$#,##0");
        SubTitle("Demand Only");
        WriteMatrix(vendorNames, "Vendor", yearLabels, (i, j, r) => VendorFormulaFromPool(poolCostYDemand, i, j), "$#,##0");
        SubTitle("Both (Allocated + Demand)");
        WriteMatrix(vendorNames, "Vendor", yearLabels, (i, j, r) => VendorFormulaFromPool(poolCostYBoth, i, j), "$#,##0");

        rowRef = row;
    }
}

