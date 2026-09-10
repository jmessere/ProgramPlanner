namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// An alternative resource plan (e.g. Baseline, Working Plan). Master data
/// (people, teams, templates, roles, workstreams) is shared across scenarios;
/// only ResourcePlanLine records are scenario-specific.
/// </summary>
public class PlanningScenario : BaseEntity
{
    public int ProgramId { get; set; }

    public Program? Program { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsBaseline { get; set; }

    public bool Active { get; set; } = true;

    public List<ResourcePlanLine> ResourcePlanLines { get; set; } = new();
}
