using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Application.Interfaces;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Backs the Resource Plan monthly grid screen. Presents ResourcePlanLines
/// as monthly rows and, when a cell is edited, re-derives the underlying
/// date-range records via ResourceTransformationService (grid and Gantt
/// share the same ResourcePlanLine records).
/// </summary>
public class ResourcePlanGridService
{
    private readonly IAppDbContext _db;
    private readonly ResourceTransformationService _engine;

    public ResourcePlanGridService(IAppDbContext db, ResourceTransformationService engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<List<ResourcePlanGridRow>> GetGridAsync(
        int scenarioId, DateOnly horizonStart, DateOnly horizonEnd, CancellationToken ct = default)
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

        var monthly = _engine.ExpandToMonthly(lines, horizonStart, horizonEnd);
        var sample = lines.GroupBy(l => l.ToRowKey()).ToDictionary(g => g.Key, g => g.First());

        var rows = new List<ResourcePlanGridRow>();
        foreach (var (key, values) in monthly)
        {
            var s = sample[key];
            var templateName = s.TeamTemplateAssignment?.Template?.Name ?? s.TemplatePhase?.Template?.Name;
            rows.Add(new ResourcePlanGridRow
            {
                Key = key,
                TeamName = s.Team?.Name ?? string.Empty,
                TemplateName = templateName,
                PhaseName = s.TemplatePhase?.Name,
                WorkstreamName = s.Workstream?.Name,
                RoleName = s.Role?.Name ?? string.Empty,
                PersonName = s.Person?.DisplayName,
                Months = values
            });
        }

        return rows
            .OrderBy(r => r.TeamName).ThenBy(r => r.RoleName).ThenBy(r => r.PersonName)
            .ToList();
    }

    /// <summary>
    /// Updates a single month's FTE for a row and re-consolidates that row's
    /// ResourcePlanLines. Existing lines for the row context within the
    /// horizon are replaced with the newly consolidated ranges.
    /// </summary>
    public async Task UpdateCellAsync(
        int programId, ResourcePlanRowKey key, DateOnly horizonStart, DateOnly horizonEnd,
        int year, int month, decimal newFte, CancellationToken ct = default)
    {
        var existingLines = await _db.ResourcePlanLines
            .Where(r => r.ScenarioId == key.ScenarioId
                && r.TeamId == key.TeamId
                && r.TeamTemplateAssignmentId == key.TeamTemplateAssignmentId
                && r.TemplatePhaseId == key.TemplatePhaseId
                && r.WorkstreamId == key.WorkstreamId
                && r.FocusAreaId == key.FocusAreaId
                && r.RoleId == key.RoleId
                && r.PersonId == key.PersonId)
            .ToListAsync(ct);

        var monthly = _engine.ExpandToMonthly(existingLines, horizonStart, horizonEnd);
        var values = monthly.Count > 0 ? monthly.Values.Single() : BuildBlankMonths(horizonStart, horizonEnd);

        var updated = values.Select(v => v.Year == year && v.Month == month ? new MonthlyValue(v.Year, v.Month, newFte) : v).ToList();
        var ranges = _engine.ConsolidateToRanges(updated);

        var notes = existingLines.FirstOrDefault()?.Notes;

        foreach (var line in existingLines)
        {
            _db.ResourcePlanLines.Remove(line);
        }

        foreach (var range in ranges)
        {
            _db.ResourcePlanLines.Add(new ResourcePlanLine
            {
                ProgramId = programId,
                ScenarioId = key.ScenarioId,
                TeamId = key.TeamId,
                TeamTemplateAssignmentId = key.TeamTemplateAssignmentId,
                TemplatePhaseId = key.TemplatePhaseId,
                WorkstreamId = key.WorkstreamId,
                FocusAreaId = key.FocusAreaId,
                RoleId = key.RoleId,
                PersonId = key.PersonId,
                StartDate = range.StartDate,
                EndDate = range.EndDate,
                Fte = range.Fte,
                Notes = notes
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private static List<MonthlyValue> BuildBlankMonths(DateOnly start, DateOnly end)
    {
        var months = new List<MonthlyValue>();
        var cursor = new DateOnly(start.Year, start.Month, 1);
        var endMonth = new DateOnly(end.Year, end.Month, 1);
        while (cursor <= endMonth)
        {
            months.Add(new MonthlyValue(cursor.Year, cursor.Month, 0m));
            cursor = cursor.AddMonths(1);
        }
        return months;
    }
}
