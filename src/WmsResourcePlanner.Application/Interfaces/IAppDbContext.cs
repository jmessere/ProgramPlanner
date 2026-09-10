using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Application.Interfaces;

/// <summary>
/// Abstraction over the EF Core DbContext so the Application layer can
/// depend on data access without referencing Infrastructure directly. The
/// concrete AppDbContext (Infrastructure) implements this interface.
/// </summary>
public interface IAppDbContext
{
    DbSet<Program> Programs { get; }

    DbSet<PlanningScenario> PlanningScenarios { get; }

    DbSet<Template> Templates { get; }

    DbSet<TemplatePhase> TemplatePhases { get; }

    DbSet<Workstream> Workstreams { get; }

    DbSet<FocusArea> FocusAreas { get; }

    DbSet<Team> Teams { get; }

    DbSet<TeamTemplateAssignment> TeamTemplateAssignments { get; }

    DbSet<Role> Roles { get; }

    DbSet<TeamRole> TeamRoles { get; }

    DbSet<Person> People { get; }

    DbSet<ResourcePool> ResourcePools { get; }

    DbSet<ResourcePlanLine> ResourcePlanLines { get; }

    DbSet<Site> Sites { get; }

    DbSet<TeamSiteAssignment> TeamSiteAssignments { get; }

    DbSet<AuditEntry> AuditEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
