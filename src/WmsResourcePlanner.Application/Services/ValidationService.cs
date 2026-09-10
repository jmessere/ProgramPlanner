using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Application.Interfaces;

namespace WmsResourcePlanner.Application.Services;

public record GridWarning(ResourcePlanRowKey Key, int Year, int Month, string Message);

/// <summary>
/// Computes inline validation warnings for the Resource Plan grid / Gantt
/// views (SPEC.md Phase 14): person overallocation, allocations outside a
/// person's declared availability window, and allocations outside the
/// bounds of their Team-Template assignment or Template phase.
/// </summary>
public class ValidationService
{
    private readonly IAppDbContext _db;
    private readonly CapacityService _capacityService;

    public ValidationService(IAppDbContext db, CapacityService capacityService)
    {
        _db = db;
        _capacityService = capacityService;
    }

    public async Task<List<GridWarning>> GetWarningsAsync(
        int scenarioId, DateOnly horizonStart, DateOnly horizonEnd, CancellationToken ct = default)
    {
        var warnings = new List<GridWarning>();

        var lines = await _db.ResourcePlanLines
            .Where(r => r.ScenarioId == scenarioId)
            .Include(r => r.Person)
            .Include(r => r.TeamTemplateAssignment)
            .Include(r => r.TemplatePhase)
            .ToListAsync(ct);

        // Overallocation: reuse CapacityService's per-person/month totals.
        var capacity = await _capacityService.GetPersonCapacityAsync(scenarioId, horizonStart, horizonEnd, ct: ct);
        var overallocatedMonths = capacity
            .Where(c => c.Status == CapacityStatus.Overallocated)
            .Select(c => (c.PersonId, c.Year, c.Month))
            .ToHashSet();

        foreach (var line in lines)
        {
            var months = MonthsInRange(line.StartDate, line.EndDate, horizonStart, horizonEnd);

            foreach (var (year, month) in months)
            {
                var key = ToKey(line, scenarioId);

                if (line.PersonId is not null && overallocatedMonths.Contains((line.PersonId.Value, year, month)))
                {
                    warnings.Add(new GridWarning(key, year, month,
                        $"{line.Person?.DisplayName} is overallocated in {new DateOnly(year, month, 1):MMM yyyy}."));
                }

                if (line.Person is { AvailableStartDate: not null } p1 && new DateOnly(year, month, 1) < p1.AvailableStartDate)
                {
                    warnings.Add(new GridWarning(key, year, month,
                        $"{p1.DisplayName} is not yet available in {new DateOnly(year, month, 1):MMM yyyy}."));
                }

                if (line.Person is { AvailableEndDate: not null } p2 && new DateOnly(year, month, 1) > p2.AvailableEndDate)
                {
                    warnings.Add(new GridWarning(key, year, month,
                        $"{p2.DisplayName} is no longer available in {new DateOnly(year, month, 1):MMM yyyy}."));
                }

                if (line.TeamTemplateAssignment is { } assignment)
                {
                    var monthStart = new DateOnly(year, month, 1);
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    if (monthEnd < assignment.StartDate || monthStart > assignment.EndDate)
                    {
                        warnings.Add(new GridWarning(key, year, month,
                            $"Allocation falls outside the team's assignment to this template ({assignment.StartDate:MMM yyyy}-{assignment.EndDate:MMM yyyy})."));
                    }
                }

                if (line.TemplatePhase is { } phase)
                {
                    var monthStart = new DateOnly(year, month, 1);
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    if (monthEnd < phase.StartDate || monthStart > phase.EndDate)
                    {
                        warnings.Add(new GridWarning(key, year, month,
                            $"Allocation falls outside phase \"{phase.Name}\" ({phase.StartDate:MMM yyyy}-{phase.EndDate:MMM yyyy})."));
                    }
                }
            }
        }

        return warnings;
    }

    private static ResourcePlanRowKey ToKey(Domain.Entities.ResourcePlanLine line, int scenarioId) => new(
        scenarioId, line.TeamId, line.TeamTemplateAssignmentId, line.TemplatePhaseId,
        line.WorkstreamId, line.FocusAreaId, line.RoleId, line.PersonId);

    private static IEnumerable<(int Year, int Month)> MonthsInRange(
        DateOnly start, DateOnly end, DateOnly horizonStart, DateOnly horizonEnd)
    {
        var clampedStart = start < horizonStart ? horizonStart : start;
        var clampedEnd = end > horizonEnd ? horizonEnd : end;
        if (clampedStart > clampedEnd) yield break;

        var cursor = new DateOnly(clampedStart.Year, clampedStart.Month, 1);
        var last = new DateOnly(clampedEnd.Year, clampedEnd.Month, 1);
        while (cursor <= last)
        {
            yield return (cursor.Year, cursor.Month);
            cursor = cursor.AddMonths(1);
        }
    }
}
