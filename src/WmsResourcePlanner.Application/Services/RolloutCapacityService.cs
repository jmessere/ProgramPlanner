using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Application.Interfaces;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Rollout-specific analysis (SPEC.md section 51): allocated/open FTE,
/// capacity, and conflicts for Rollout-type teams, sliceable by Template,
/// Rollout Team, Site, Role, Person, and Month.
/// </summary>
public class RolloutCapacityService
{
    private readonly IAppDbContext _db;
    private readonly ResourceTransformationService _engine;

    public RolloutCapacityService(IAppDbContext db, ResourceTransformationService engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<List<RolloutCapacityResult>> GetRolloutCapacityAsync(
        int scenarioId, DateOnly horizonStart, DateOnly horizonEnd, CancellationToken ct = default)
    {
        var lines = await _db.ResourcePlanLines
            .Where(r => r.ScenarioId == scenarioId && r.Team!.TeamType == "Rollout")
            .Include(r => r.Team)
            .Include(r => r.Role)
            .Include(r => r.Person)
            .Include(r => r.TeamTemplateAssignment).ThenInclude(a => a!.Template)
            .Include(r => r.TemplatePhase).ThenInclude(p => p!.Template)
            .ToListAsync(ct);

        if (lines.Count == 0)
        {
            return new List<RolloutCapacityResult>();
        }

        var teamIds = lines.Select(l => l.TeamId).Distinct().ToList();
        var siteAssignments = await _db.TeamSiteAssignments
            .Where(a => teamIds.Contains(a.TeamId))
            .Include(a => a.Site)
            .ToListAsync(ct);

        var people = await _db.People.ToDictionaryAsync(p => p.Id, ct);

        var monthly = _engine.ExpandToMonthly(lines, horizonStart, horizonEnd);
        var lookup = lines.GroupBy(l => l.ToRowKey()).ToDictionary(g => g.Key, g => g.First());

        // Track per-person/month allocation to flag overallocation conflicts.
        var perPersonMonth = new Dictionary<(int PersonId, int Year, int Month), decimal>();
        foreach (var (key, values) in monthly)
        {
            if (key.PersonId is null) continue;
            foreach (var v in values)
            {
                var mk = (key.PersonId.Value, v.Year, v.Month);
                perPersonMonth.TryGetValue(mk, out var existing);
                perPersonMonth[mk] = existing + v.Fte;
            }
        }

        var results = new List<RolloutCapacityResult>();
        foreach (var (key, values) in monthly)
        {
            if (!lookup.TryGetValue(key, out var sample))
            {
                continue;
            }

            var templateName = sample.TeamTemplateAssignment?.Template?.Name
                ?? sample.TemplatePhase?.Template?.Name;

            foreach (var v in values.Where(v => v.Fte != 0m))
            {
                var monthDate = new DateOnly(v.Year, v.Month, 1);
                var site = siteAssignments.FirstOrDefault(a =>
                    a.TeamId == sample.TeamId && a.StartDate <= monthDate && a.EndDate >= monthDate);

                decimal? personCapacity = null;
                var conflict = false;
                if (key.PersonId is not null && people.TryGetValue(key.PersonId.Value, out var person))
                {
                    personCapacity = person.DefaultCapacityFte;
                    var allocated = perPersonMonth[(key.PersonId.Value, v.Year, v.Month)];
                    conflict = allocated > person.DefaultCapacityFte;
                }

                results.Add(new RolloutCapacityResult(
                    v.Year,
                    v.Month,
                    templateName,
                    sample.Team?.Name ?? string.Empty,
                    site?.Site?.Name,
                    sample.Role?.Name ?? string.Empty,
                    sample.Person?.DisplayName,
                    key.PersonId is null ? 0m : v.Fte,
                    key.PersonId is null ? v.Fte : 0m,
                    personCapacity,
                    conflict));
            }
        }

        return results
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .ThenBy(r => r.TeamName).ThenBy(r => r.RoleName)
            .ToList();
    }
}
