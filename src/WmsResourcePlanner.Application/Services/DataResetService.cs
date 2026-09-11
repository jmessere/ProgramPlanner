using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Interfaces;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Supports the Settings page's "Clear All Data" danger-zone action: wipes
/// every planning entity (Teams, People, Roles, Templates, Template Phases,
/// Workstreams, Focus Areas, Sites, Resource Pools, Team-Template
/// Assignments, Team Roles, Team-Site Assignments, Resource Plan Lines,
/// Planning Scenarios, Audit Entries) so the app starts fresh, without
/// requiring a full application restart/re-migration. The single Program
/// row itself (and its Name/Description/planning horizon) is preserved,
/// since the rest of the app assumes exactly one Program always exists -
/// and a brand new "Baseline" Planning Scenario is created immediately
/// afterward, since most pages assume at least one baseline scenario
/// exists for the current Program.
/// </summary>
public class DataResetService
{
    private readonly IAppDbContext _db;

    public DataResetService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task ClearAllDataAsync(CancellationToken ct = default)
    {
        var program = await _db.Programs.FirstAsync(ct);

        // Deleted in child-to-parent order so no FK constraint is ever
        // violated, even though ExecuteDeleteAsync issues one DELETE per
        // call rather than relying on EF's change-tracker ordering.
        await _db.ResourcePlanLines.ExecuteDeleteAsync(ct);
        await _db.TeamRoles.ExecuteDeleteAsync(ct);
        await _db.TeamSiteAssignments.ExecuteDeleteAsync(ct);
        await _db.TeamTemplateAssignments.ExecuteDeleteAsync(ct);
        await _db.TemplatePhases.ExecuteDeleteAsync(ct);
        await _db.Templates.ExecuteDeleteAsync(ct);
        await _db.Teams.ExecuteDeleteAsync(ct);
        await _db.FocusAreas.ExecuteDeleteAsync(ct);
        await _db.Workstreams.ExecuteDeleteAsync(ct);
        await _db.People.ExecuteDeleteAsync(ct);
        await _db.ResourcePools.ExecuteDeleteAsync(ct);
        await _db.Sites.ExecuteDeleteAsync(ct);
        await _db.Roles.ExecuteDeleteAsync(ct);
        await _db.PlanningScenarios.ExecuteDeleteAsync(ct);
        await _db.AuditEntries.ExecuteDeleteAsync(ct);

        _db.PlanningScenarios.Add(new PlanningScenario
        {
            ProgramId = program.Id,
            Name = "Baseline",
            IsBaseline = true,
            Active = true
        });
        await _db.SaveChangesAsync(ct);
    }
}
