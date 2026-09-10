namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// A functional area of work (e.g. Inbound, Outbound, Automation). Examples
/// only - not a hard-coded restriction.
/// </summary>
public class Workstream : BaseEntity
{
    public int ProgramId { get; set; }

    public Program? Program { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool Active { get; set; } = true;

    public string? Notes { get; set; }

    public List<FocusArea> FocusAreas { get; set; } = new();

    public List<Team> Teams { get; set; } = new();

    public List<ResourcePlanLine> ResourcePlanLines { get; set; } = new();
}
