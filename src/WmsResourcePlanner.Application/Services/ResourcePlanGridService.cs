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
    private readonly UndoService _undo;
    private readonly AuditService _audit;

    public ResourcePlanGridService(IAppDbContext db, ResourceTransformationService engine, UndoService undo, AuditService audit)
    {
        _db = db;
        _engine = engine;
        _undo = undo;
        _audit = audit;
    }

    public async Task<List<ResourcePlanGridRow>> GetGridAsync(
        int scenarioId, DateOnly horizonStart, DateOnly horizonEnd, CancellationToken ct = default)
    {
        var lines = await _db.ResourcePlanLines
            .Where(r => r.ScenarioId == scenarioId)
            .Include(r => r.Team)
            .Include(r => r.Role)
            .Include(r => r.Person).ThenInclude(p => p!.ResourcePool)
            .Include(r => r.ResourcePool)
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
                PoolName = s.Person?.ResourcePool?.Name ?? s.ResourcePool?.Name,
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
                && r.PersonId == key.PersonId
                && r.ResourcePoolId == key.ResourcePoolId)
            .ToListAsync(ct);

        var monthly = _engine.ExpandToMonthly(existingLines, horizonStart, horizonEnd);
        var values = monthly.Count > 0 ? monthly.Values.Single() : BuildBlankMonths(horizonStart, horizonEnd);

        var updated = values.Select(v => v.Year == year && v.Month == month ? new MonthlyValue(v.Year, v.Month, newFte) : v).ToList();
        var ranges = _engine.ConsolidateToRanges(updated);

        var notes = existingLines.FirstOrDefault()?.Notes;

        // Snapshot the pre-edit state so this change can be undone.
        var snapshot = existingLines.Select(l => new ResourcePlanLine
        {
            ProgramId = l.ProgramId, ScenarioId = l.ScenarioId, TeamId = l.TeamId,
            TeamTemplateAssignmentId = l.TeamTemplateAssignmentId, TemplatePhaseId = l.TemplatePhaseId,
            WorkstreamId = l.WorkstreamId, FocusAreaId = l.FocusAreaId, RoleId = l.RoleId, PersonId = l.PersonId,
            ResourcePoolId = l.ResourcePoolId,
            StartDate = l.StartDate, EndDate = l.EndDate, Fte = l.Fte, Notes = l.Notes
        }).ToList();

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
                ResourcePoolId = key.ResourcePoolId,
                StartDate = range.StartDate,
                EndDate = range.EndDate,
                Fte = range.Fte,
                Notes = notes
            });
        }

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            "ResourcePlanLine",
            key.TeamId,
            "GridCellEdit",
            new { existingLines.FirstOrDefault()?.Fte, Year = year, Month = month },
            new { NewFte = newFte, Year = year, Month = month },
            ct);

        _undo.Push($"Resource Plan cell edit ({year}-{month:00})", async () =>
        {
            var current = await _db.ResourcePlanLines
                .Where(r => r.ScenarioId == key.ScenarioId
                    && r.TeamId == key.TeamId
                    && r.TeamTemplateAssignmentId == key.TeamTemplateAssignmentId
                    && r.TemplatePhaseId == key.TemplatePhaseId
                    && r.WorkstreamId == key.WorkstreamId
                    && r.FocusAreaId == key.FocusAreaId
                    && r.RoleId == key.RoleId
                    && r.PersonId == key.PersonId
                    && r.ResourcePoolId == key.ResourcePoolId)
                .ToListAsync();
            foreach (var line in current) _db.ResourcePlanLines.Remove(line);
            foreach (var s in snapshot)
            {
                _db.ResourcePlanLines.Add(new ResourcePlanLine
                {
                    ProgramId = s.ProgramId, ScenarioId = s.ScenarioId, TeamId = s.TeamId,
                    TeamTemplateAssignmentId = s.TeamTemplateAssignmentId, TemplatePhaseId = s.TemplatePhaseId,
                    WorkstreamId = s.WorkstreamId, FocusAreaId = s.FocusAreaId, RoleId = s.RoleId, PersonId = s.PersonId,
                    ResourcePoolId = s.ResourcePoolId,
                    StartDate = s.StartDate, EndDate = s.EndDate, Fte = s.Fte, Notes = s.Notes
                });
            }
            await _db.SaveChangesAsync();
        });
    }

    /// <summary>
    /// Re-points every ResourcePlanLine that makes up a grid row to a new
    /// Team/Template-assignment/Workstream/Role/Person combination, so the
    /// leading (frozen) columns can be edited in place like a spreadsheet.
    /// If the new combination collides with another existing row, the lines
    /// simply merge under that row key (ExpandToMonthly sums FTE for lines
    /// that share a key), which is safe and non-destructive.
    /// </summary>
    public async Task UpdateRowContextAsync(
        ResourcePlanRowKey oldKey,
        int newTeamId, int? newAssignmentId, int? newWorkstreamId, int newRoleId, int? newPersonId,
        int? newResourcePoolId = null,
        CancellationToken ct = default)
    {
        var lines = await _db.ResourcePlanLines
            .Where(r => r.ScenarioId == oldKey.ScenarioId
                && r.TeamId == oldKey.TeamId
                && r.TeamTemplateAssignmentId == oldKey.TeamTemplateAssignmentId
                && r.TemplatePhaseId == oldKey.TemplatePhaseId
                && r.WorkstreamId == oldKey.WorkstreamId
                && r.FocusAreaId == oldKey.FocusAreaId
                && r.RoleId == oldKey.RoleId
                && r.PersonId == oldKey.PersonId
                && r.ResourcePoolId == oldKey.ResourcePoolId)
            .ToListAsync(ct);

        if (lines.Count == 0) return;

        var original = lines.Select(l => (
            l.Id, l.TeamId, l.TeamTemplateAssignmentId, l.WorkstreamId, l.RoleId, l.PersonId, l.ResourcePoolId)).ToList();

        foreach (var line in lines)
        {
            line.TeamId = newTeamId;
            line.TeamTemplateAssignmentId = newAssignmentId;
            line.WorkstreamId = newWorkstreamId;
            line.RoleId = newRoleId;
            line.PersonId = newPersonId;
            line.ResourcePoolId = newPersonId is null ? newResourcePoolId : null;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            "ResourcePlanLine",
            newTeamId,
            "GridRowContextEdit",
            new { oldKey.TeamId, oldKey.TeamTemplateAssignmentId, oldKey.WorkstreamId, oldKey.RoleId, oldKey.PersonId, oldKey.ResourcePoolId },
            new { newTeamId, newAssignmentId, newWorkstreamId, newRoleId, newPersonId, newResourcePoolId },
            ct);

        _undo.Push("Resource Plan row edit", async () =>
        {
            var ids = original.Select(o => o.Id).ToList();
            var current = await _db.ResourcePlanLines.Where(l => ids.Contains(l.Id)).ToListAsync();
            foreach (var line in current)
            {
                var o = original.First(x => x.Id == line.Id);
                line.TeamId = o.TeamId;
                line.TeamTemplateAssignmentId = o.TeamTemplateAssignmentId;
                line.WorkstreamId = o.WorkstreamId;
                line.RoleId = o.RoleId;
                line.PersonId = o.PersonId;
                line.ResourcePoolId = o.ResourcePoolId;
            }
            await _db.SaveChangesAsync();
        });
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
