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
        BuildResourcePlanSheet(workbook, monthly, sample, months);
        await BuildTemplatePlanSheetAsync(workbook, months, ct);
        var refCols = await BuildReferenceDataSheetAsync(workbook, pools, ct);
        BuildSummarySheet(workbook, months, lines, pools, refCols);

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
        ws.Cell(5, 1).Value = "A populated Person means a named allocation. A blank Person means open (unfilled) demand; its Pool column names the proposed sourcing pool.";
        ws.Cell(6, 1).Value = "Template Plan: template phases with their date ranges, filterable, and a monthly timeline where an \"X\" marks the phase's active months (colored like a Gantt bar). Team assignments are not shown here - they are implied by the Team values already present on the Resource Plan sheet.";
        ws.Cell(7, 1).Value = "Reference Data: master lists (people, teams, roles, templates, workstreams, etc.).";
        ws.Cell(8, 1).Value = "Summary: filled/open FTE by month, gaps by role/team/template, overallocated people, and Pool/Vendor cost & headcount rollups by quarter and year (all live formulas).";
        ws.Columns().AdjustToContents();
    }

    private void BuildResourcePlanSheet(
        XLWorkbook workbook,
        Dictionary<Application.DTOs.ResourcePlanRowKey, List<Application.DTOs.MonthlyValue>> monthly,
        Dictionary<Application.DTOs.ResourcePlanRowKey, ResourcePlanLine> sample,
        List<DateOnly> months)
    {
        var ws = workbook.Worksheets.Add("Resource Plan");

        string[] fixedHeaders = { "Template", "Phase", "Workstream", "Focus Area", "Team", "Role", "Person", "Pool", "Notes" };
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

    private void BuildSummarySheet(XLWorkbook workbook, List<DateOnly> months, List<ResourcePlanLine> lines, List<ResourcePool> pools, ReferenceDataColumns refCols)
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

        row += 2;
        BuildPoolAndVendorSections(ws, rpWs, ref row, months, lines, pools, refCols, rpLastRow, ColLetter, RpRange, personRange);

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

        rpWs.Cell(1, effPoolCol).Value = "Effective Pool";
        rpWs.Cell(1, effVendorCol).Value = "Effective Vendor";
        rpWs.Cell(1, effRateCol).Value = "Effective Hourly Rate";
        rpWs.Row(1).Style.Font.SetBold();

        var personColLetter = colLetter(RpPersonCol);
        var poolColLetter = colLetter(RpPoolCol);
        var effPoolColLetter = colLetter(effPoolCol);
        var effRateColLetter = colLetter(effRateCol);

        var peopleRange = $"'Reference Data'!${colLetter(refCols.PeopleCol)}$3:${colLetter(refCols.PeopleCol + 1)}${RpMaxDataRow}";
        var poolsLookupRange = $"'Reference Data'!${colLetter(refCols.ResourcePoolsCol)}$3:${colLetter(refCols.ResourcePoolsCol + 5)}${RpMaxDataRow}";

        for (var r = 2; r <= rpLastRow; r++)
        {
            rpWs.Cell(r, effPoolCol).FormulaA1 =
                $"=IF(${personColLetter}{r}<>\"\",IFERROR(VLOOKUP(${personColLetter}{r},{peopleRange},2,FALSE),\"\"),${poolColLetter}{r})";
            rpWs.Cell(r, effRateCol).FormulaA1 =
                $"=IF(${effPoolColLetter}{r}<>\"\",IFERROR(VLOOKUP(${effPoolColLetter}{r},{poolsLookupRange},4,FALSE),0),0)";
            rpWs.Cell(r, effVendorCol).FormulaA1 =
                $"=IF(${effPoolColLetter}{r}<>\"\",IFERROR(VLOOKUP(${effPoolColLetter}{r},{poolsLookupRange},5,FALSE),\"\"),\"\")";
        }
        rpWs.Range(2, effRateCol, Math.Max(2, rpLastRow), effRateCol).Style.NumberFormat.Format = "$#,##0.00";

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
            rpWs.Cell(1, quarterCostCol[q]).Value = $"{quarterLabels[q]} Cost (helper)";
            rpWs.Cell(1, quarterCountCol[q]).Value = $"{quarterLabels[q]} Count (helper)";
        }

        for (var r = 2; r <= rpLastRow; r++)
        {
            for (var q = 0; q < quarters.Count; q++)
            {
                var idxs = quarterMonthIdx[q];
                var monthRange = $"{colLetter(RpFirstMonthCol + idxs[0])}{r}:{colLetter(RpFirstMonthCol + idxs[^1])}{r}";
                rpWs.Cell(r, quarterCostCol[q]).FormulaA1 = $"=SUM({monthRange})*(2080/12)*${effRateColLetter}{r}";
                rpWs.Cell(r, quarterCountCol[q]).FormulaA1 = $"=AVERAGE({monthRange})";
            }
        }

        if (quarters.Count > 0)
        {
            rpWs.Range(2, quarterCostCol[0], Math.Max(2, rpLastRow), quarterCostCol[^1]).Style.NumberFormat.Format = "$#,##0";
            rpWs.Range(2, quarterCountCol[0], Math.Max(2, rpLastRow), quarterCountCol[^1]).Style.NumberFormat.Format = "0.00";
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

