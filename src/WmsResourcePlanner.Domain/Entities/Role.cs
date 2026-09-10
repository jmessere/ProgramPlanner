namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// A required skillset/role (Business Analyst, Developer, QA, ...).
/// Dynamically creatable.
/// </summary>
public class Role : BaseEntity
{
    public int ProgramId { get; set; }

    public Program? Program { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }

    public string? Description { get; set; }

    public bool Active { get; set; } = true;

    public List<TeamRole> TeamRoles { get; set; } = new();

    public List<ResourcePlanLine> ResourcePlanLines { get; set; } = new();
}
