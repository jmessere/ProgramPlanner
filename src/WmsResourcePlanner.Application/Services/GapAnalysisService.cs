using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Application.Interfaces;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Aggregates open (unfilled) demand - ResourcePlanLines where PersonId is
/// null - by month/template/workstream/team/role.
/// </summary>
public class GapAnalysisService
{
    private readonly IAppDbContext _db;
    private readonly ResourceTransformationService _engine;

    public GapAnalysisService(IAppDbContext db, ResourceTransformationService engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<List<GapResult>> GetOpenDemandAsync(
        int scenarioId, DateOnly horizonStart, DateOnly horizonEnd, CancellationToken ct = default)
    {
        var lines = await _db.ResourcePlanLines
            .Where(r => r.ScenarioId == scenarioId && r.PersonId == null)
            .Include(r => r.Team)
            .Include(r => r.Role)
            .Include(r => r.Workstream)
            .Include(r => r.TeamTemplateAssignment).ThenInclude(a => a!.Template)
            .Include(r => r.TemplatePhase).ThenInclude(p => p!.Template)
            .ToListAsync(ct);

        var monthly = _engine.ExpandToMonthly(lines, horizonStart, horizonEnd);

        var lookup = lines.GroupBy(l => l.ToRowKey()).ToDictionary(g => g.Key, g => g.First());

        var results = new List<GapResult>();
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
                results.Add(new GapResult(
                    v.Year,
                    v.Month,
                    templateName,
                    sample.Workstream?.Name,
                    sample.Team?.Name ?? string.Empty,
                    sample.Role?.Name ?? string.Empty,
                    v.Fte));
            }
        }

        return results
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .ThenBy(r => r.TeamName).ThenBy(r => r.RoleName)
            .ToList();
    }
}
