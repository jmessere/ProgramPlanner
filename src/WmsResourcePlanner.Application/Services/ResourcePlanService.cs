using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Interfaces;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// CRUD and query operations for ResourcePlanLine, the core planning entity.
/// </summary>
public class ResourcePlanService
{
    private readonly IAppDbContext _db;

    public ResourcePlanService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<ResourcePlanLine> CreateAsync(ResourcePlanLine line, CancellationToken ct = default)
    {
        line.CreatedUtc = DateTime.UtcNow;
        line.ModifiedUtc = DateTime.UtcNow;
        _db.ResourcePlanLines.Add(line);
        await _db.SaveChangesAsync(ct);
        return line;
    }

    public async Task<ResourcePlanLine?> UpdateAsync(int id, Action<ResourcePlanLine> mutate, CancellationToken ct = default)
    {
        var line = await _db.ResourcePlanLines.FindAsync([id], ct);
        if (line is null)
        {
            return null;
        }

        mutate(line);
        line.ModifiedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return line;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var line = await _db.ResourcePlanLines.FindAsync([id], ct);
        if (line is null)
        {
            return false;
        }

        _db.ResourcePlanLines.Remove(line);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public IQueryable<ResourcePlanLine> QueryByScenario(int scenarioId) =>
        _db.ResourcePlanLines.Where(r => r.ScenarioId == scenarioId);

    public IQueryable<ResourcePlanLine> QueryByTemplate(int templateId) =>
        _db.ResourcePlanLines.Where(r =>
            (r.TeamTemplateAssignment != null && r.TeamTemplateAssignment.TemplateId == templateId) ||
            (r.TemplatePhase != null && r.TemplatePhase.TemplateId == templateId));

    public IQueryable<ResourcePlanLine> QueryByTeam(int teamId) =>
        _db.ResourcePlanLines.Where(r => r.TeamId == teamId);

    public IQueryable<ResourcePlanLine> QueryByPerson(int personId) =>
        _db.ResourcePlanLines.Where(r => r.PersonId == personId);

    public IQueryable<ResourcePlanLine> QueryByRole(int roleId) =>
        _db.ResourcePlanLines.Where(r => r.RoleId == roleId);

    public IQueryable<ResourcePlanLine> QueryByWorkstream(int workstreamId) =>
        _db.ResourcePlanLines.Where(r => r.WorkstreamId == workstreamId);

    public IQueryable<ResourcePlanLine> QueryByDateRange(DateOnly start, DateOnly end) =>
        _db.ResourcePlanLines.Where(r => r.StartDate <= end && r.EndDate >= start);

    public IQueryable<ResourcePlanLine> QueryOpenDemand(int scenarioId) =>
        _db.ResourcePlanLines.Where(r => r.ScenarioId == scenarioId && r.PersonId == null);

    public IQueryable<ResourcePlanLine> IncludeAll(IQueryable<ResourcePlanLine> query) =>
        query
            .Include(r => r.Team)
            .Include(r => r.TeamTemplateAssignment).ThenInclude(a => a!.Template)
            .Include(r => r.TemplatePhase)
            .Include(r => r.Workstream)
            .Include(r => r.FocusArea)
            .Include(r => r.Role)
            .Include(r => r.Person);
}
