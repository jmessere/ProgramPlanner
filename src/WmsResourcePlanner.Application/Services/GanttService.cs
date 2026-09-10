using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Application.Interfaces;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Backs the three Gantt views (Template, Team-Template, Resource). Reads
/// and writes the same underlying records edited by the master-data pages
/// and the Resource Plan grid, so Grid/Gantt/detail-page edits stay
/// consistent (SPEC.md: "Grid and Gantt edit the same underlying records").
/// </summary>
public class GanttService
{
    private readonly IAppDbContext _db;
    private readonly UndoService _undo;

    public GanttService(IAppDbContext db, UndoService undo)
    {
        _db = db;
        _undo = undo;
    }

    // ---- Template Gantt: TemplatePhase bars grouped by Template ----

    public async Task<List<GanttBar>> GetTemplatePhaseBarsAsync(CancellationToken ct = default)
    {
        var phases = await _db.TemplatePhases
            .Include(p => p.Template)
            .OrderBy(p => p.Template!.Name).ThenBy(p => p.SortOrder)
            .ToListAsync(ct);

        return phases.Select(p => new GanttBar
        {
            Id = p.Id,
            GroupLabel = p.Template?.Name ?? "(no template)",
            BarLabel = p.Name,
            StartDate = p.StartDate,
            EndDate = p.EndDate
        }).ToList();
    }

    public async Task UpdateTemplatePhaseDatesAsync(int phaseId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var phase = await _db.TemplatePhases.FirstOrDefaultAsync(p => p.Id == phaseId, ct);
        if (phase is null) return;
        if (end < start) end = start;
        var (prevStart, prevEnd) = (phase.StartDate, phase.EndDate);
        phase.StartDate = start;
        phase.EndDate = end;
        await _db.SaveChangesAsync(ct);

        _undo.Push($"Move/resize template phase \"{phase.Name}\"", async () =>
        {
            var p = await _db.TemplatePhases.FirstOrDefaultAsync(x => x.Id == phaseId);
            if (p is null) return;
            p.StartDate = prevStart;
            p.EndDate = prevEnd;
            await _db.SaveChangesAsync();
        });
    }

    // ---- Team-Template Gantt: TeamTemplateAssignment bars grouped by Team ----

    public async Task<List<GanttBar>> GetTeamTemplateAssignmentBarsAsync(CancellationToken ct = default)
    {
        var assignments = await _db.TeamTemplateAssignments
            .Include(a => a.Team)
            .Include(a => a.Template)
            .OrderBy(a => a.Team!.Name).ThenBy(a => a.StartDate)
            .ToListAsync(ct);

        return assignments.Select(a => new GanttBar
        {
            Id = a.Id,
            GroupLabel = a.Team?.Name ?? "(no team)",
            BarLabel = a.Template?.Name ?? "(no template)",
            StartDate = a.StartDate,
            EndDate = a.EndDate
        }).ToList();
    }

    public async Task UpdateTeamTemplateAssignmentDatesAsync(int assignmentId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var assignment = await _db.TeamTemplateAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
        if (assignment is null) return;
        if (end < start) end = start;
        var (prevStart, prevEnd) = (assignment.StartDate, assignment.EndDate);
        assignment.StartDate = start;
        assignment.EndDate = end;
        await _db.SaveChangesAsync(ct);

        _undo.Push("Move/resize team-template assignment", async () =>
        {
            var a = await _db.TeamTemplateAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId);
            if (a is null) return;
            a.StartDate = prevStart;
            a.EndDate = prevEnd;
            await _db.SaveChangesAsync();
        });
    }

    // ---- Resource Gantt: ResourcePlanLine bars grouped by Person (or Team/Role if open demand) ----

    public async Task<List<GanttBar>> GetResourcePlanLineBarsAsync(int scenarioId, CancellationToken ct = default)
    {
        var lines = await _db.ResourcePlanLines
            .Where(r => r.ScenarioId == scenarioId)
            .Include(r => r.Team)
            .Include(r => r.Role)
            .Include(r => r.Person)
            .Include(r => r.TeamTemplateAssignment).ThenInclude(a => a!.Template)
            .Include(r => r.TemplatePhase).ThenInclude(p => p!.Template)
            .OrderBy(r => r.Person != null ? r.Person.DisplayName : r.Team!.Name)
            .ThenBy(r => r.StartDate)
            .ToListAsync(ct);

        return lines.Select(r => new GanttBar
        {
            Id = r.Id,
            GroupLabel = r.Person?.DisplayName ?? $"Open: {r.Team?.Name} / {r.Role?.Name}",
            BarLabel = $"{r.Role?.Name} · {r.Fte:0.##} FTE",
            SubLabel = r.TeamTemplateAssignment?.Template?.Name ?? r.TemplatePhase?.Template?.Name,
            StartDate = r.StartDate,
            EndDate = r.EndDate,
            Color = r.PersonId is null ? "open" : "filled"
        }).ToList();
    }

    /// <summary>
    /// Moves/resizes a single ResourcePlanLine, preserving its FTE value
    /// (SPEC.md Acceptance Scenario F: dragging updates the date range while
    /// preserving FTE).
    /// </summary>
    public async Task UpdateResourcePlanLineDatesAsync(int lineId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var line = await _db.ResourcePlanLines.FirstOrDefaultAsync(r => r.Id == lineId, ct);
        if (line is null) return;
        if (end < start) end = start;
        var (prevStart, prevEnd) = (line.StartDate, line.EndDate);
        line.StartDate = start;
        line.EndDate = end;
        await _db.SaveChangesAsync(ct);

        _undo.Push("Move/resize resource allocation", async () =>
        {
            var l = await _db.ResourcePlanLines.FirstOrDefaultAsync(x => x.Id == lineId);
            if (l is null) return;
            l.StartDate = prevStart;
            l.EndDate = prevEnd;
            await _db.SaveChangesAsync();
        });
    }

    // ---- Global Timeline: all dimensions available for flexible grouping/filtering (SPEC.md section 48) ----

    public async Task<List<TimelineLine>> GetTimelineLinesAsync(int scenarioId, CancellationToken ct = default)
    {
        var lines = await _db.ResourcePlanLines
            .Where(r => r.ScenarioId == scenarioId)
            .Include(r => r.Team)
            .Include(r => r.Role)
            .Include(r => r.Person)
            .Include(r => r.Workstream)
            .Include(r => r.TeamTemplateAssignment).ThenInclude(a => a!.Template)
            .Include(r => r.TemplatePhase).ThenInclude(p => p!.Template)
            .ToListAsync(ct);

        return lines.Select(r => new TimelineLine
        {
            Id = r.Id,
            TemplateName = r.TeamTemplateAssignment?.Template?.Name ?? r.TemplatePhase?.Template?.Name,
            PhaseName = r.TemplatePhase?.Name,
            WorkstreamName = r.Workstream?.Name,
            TeamName = r.Team?.Name ?? string.Empty,
            RoleName = r.Role?.Name ?? string.Empty,
            PersonName = r.Person?.DisplayName,
            EmployeeType = r.Person?.EmployeeType,
            TeamType = r.Team?.TeamType,
            StartDate = r.StartDate,
            EndDate = r.EndDate,
            Fte = r.Fte
        }).ToList();
    }
}
