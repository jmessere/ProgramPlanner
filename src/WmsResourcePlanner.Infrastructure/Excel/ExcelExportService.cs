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
        await BuildTemplatePlanSheetAsync(workbook, ct);
        await BuildReferenceDataSheetAsync(workbook, ct);
        await BuildSummarySheetAsync(workbook, scenarioId, horizonStart, horizonEnd, ct);

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
        ws.Cell(4, 1).Value = "Resource Plan: one row per planning line. Monthly columns hold FTE (0.25 = quarter FTE, 1.0 = one FTE).";
        ws.Cell(5, 1).Value = "A populated Person means a named allocation. A blank Person means open (unfilled) demand.";
        ws.Cell(6, 1).Value = "Template Plan: template phases and team assignments with their date ranges.";
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
    }

    private async Task BuildTemplatePlanSheetAsync(XLWorkbook workbook, CancellationToken ct)
    {
        var ws = workbook.Worksheets.Add("Template Plan");
        string[] headers = { "Template", "Phase", "Team", "Start Date", "End Date", "Notes" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.SetBold();

        var row = 2;

        var phases = await _db.TemplatePhases.Include(p => p.Template).OrderBy(p => p.Template!.Name).ThenBy(p => p.SortOrder).ToListAsync(ct);
        foreach (var p in phases)
        {
            ws.Cell(row, 1).Value = p.Template?.Name;
            ws.Cell(row, 2).Value = p.Name;
            ws.Cell(row, 4).Value = p.StartDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 4).Style.DateFormat.Format = "mmm-yy";
            ws.Cell(row, 5).Value = p.EndDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 5).Style.DateFormat.Format = "mmm-yy";
            ws.Cell(row, 6).Value = p.Notes;
            row++;
        }

        var assignments = await _db.TeamTemplateAssignments.Include(a => a.Template).Include(a => a.Team).OrderBy(a => a.Template!.Name).ToListAsync(ct);
        foreach (var a in assignments)
        {
            ws.Cell(row, 1).Value = a.Template?.Name;
            ws.Cell(row, 3).Value = a.Team?.Name;
            ws.Cell(row, 4).Value = a.StartDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 4).Style.DateFormat.Format = "mmm-yy";
            ws.Cell(row, 5).Value = a.EndDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 5).Style.DateFormat.Format = "mmm-yy";
            ws.Cell(row, 6).Value = a.Notes;
            row++;
        }

        ws.Columns().AdjustToContents();
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

    private async Task BuildSummarySheetAsync(XLWorkbook workbook, int scenarioId, DateOnly horizonStart, DateOnly horizonEnd, CancellationToken ct)
    {
        var ws = workbook.Worksheets.Add("Summary");

        var lines = await _db.ResourcePlanLines.Where(r => r.ScenarioId == scenarioId)
            .Include(r => r.Role).Include(r => r.Team).ToListAsync(ct);

        var filled = _engine.ExpandToMonthly(lines.Where(l => l.PersonId != null), horizonStart, horizonEnd);
        var open = _engine.ExpandToMonthly(lines.Where(l => l.PersonId == null), horizonStart, horizonEnd);

        ws.Cell(1, 1).Value = "Filled FTE by Month";
        ws.Cell(1, 1).Style.Font.SetBold();
        ws.Cell(2, 1).Value = "Month";
        ws.Cell(2, 2).Value = "Filled FTE";
        ws.Cell(2, 3).Value = "Open FTE";
        ws.Cell(2, 4).Value = "Total Need";

        var months = new List<DateOnly>();
        var cursor = new DateOnly(horizonStart.Year, horizonStart.Month, 1);
        var end = new DateOnly(horizonEnd.Year, horizonEnd.Month, 1);
        while (cursor <= end) { months.Add(cursor); cursor = cursor.AddMonths(1); }

        var row = 3;
        foreach (var m in months)
        {
            var filledFte = filled.Values.SelectMany(v => v).Where(v => v.Year == m.Year && v.Month == m.Month).Sum(v => v.Fte);
            var openFte = open.Values.SelectMany(v => v).Where(v => v.Year == m.Year && v.Month == m.Month).Sum(v => v.Fte);
            ws.Cell(row, 1).Value = m.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 1).Style.DateFormat.Format = "mmm-yy";
            ws.Cell(row, 2).Value = filledFte;
            ws.Cell(row, 3).Value = openFte;
            ws.Cell(row, 4).Value = filledFte + openFte;
            row++;
        }

        row += 2;
        ws.Cell(row, 1).Value = "Open FTE by Role (current)";
        ws.Cell(row, 1).Style.Font.SetBold();
        row++;
        foreach (var g in lines.Where(l => l.PersonId == null).GroupBy(l => l.Role?.Name ?? "(unspecified)"))
        {
            ws.Cell(row, 1).Value = g.Key;
            ws.Cell(row, 2).Value = g.Sum(l => l.Fte);
            row++;
        }

        row += 1;
        ws.Cell(row, 1).Value = "Open FTE by Team (current)";
        ws.Cell(row, 1).Style.Font.SetBold();
        row++;
        foreach (var g in lines.Where(l => l.PersonId == null).GroupBy(l => l.Team?.Name ?? "(unspecified)"))
        {
            ws.Cell(row, 1).Value = g.Key;
            ws.Cell(row, 2).Value = g.Sum(l => l.Fte);
            row++;
        }

        ws.Columns().AdjustToContents();
    }
}
