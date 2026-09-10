using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Interfaces;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Manages PlanningScenarios. Duplicating a scenario copies only its
/// ResourcePlanLines - master data (people, teams, templates, roles,
/// workstreams) is shared and never duplicated.
/// </summary>
public class ScenarioService
{
    private readonly IAppDbContext _db;

    public ScenarioService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PlanningScenario> DuplicateAsync(int scenarioId, string newName, CancellationToken ct = default)
    {
        var source = await _db.PlanningScenarios.FirstAsync(s => s.Id == scenarioId, ct);

        var copy = new PlanningScenario
        {
            ProgramId = source.ProgramId,
            Name = newName,
            IsBaseline = false,
            Active = true
        };
        _db.PlanningScenarios.Add(copy);
        await _db.SaveChangesAsync(ct);

        var sourceLines = await _db.ResourcePlanLines.Where(r => r.ScenarioId == scenarioId).ToListAsync(ct);
        foreach (var line in sourceLines)
        {
            _db.ResourcePlanLines.Add(new ResourcePlanLine
            {
                ProgramId = line.ProgramId,
                ScenarioId = copy.Id,
                TeamId = line.TeamId,
                TeamTemplateAssignmentId = line.TeamTemplateAssignmentId,
                TemplatePhaseId = line.TemplatePhaseId,
                WorkstreamId = line.WorkstreamId,
                FocusAreaId = line.FocusAreaId,
                RoleId = line.RoleId,
                PersonId = line.PersonId,
                StartDate = line.StartDate,
                EndDate = line.EndDate,
                Fte = line.Fte,
                Notes = line.Notes
            });
        }

        await _db.SaveChangesAsync(ct);
        return copy;
    }
}
