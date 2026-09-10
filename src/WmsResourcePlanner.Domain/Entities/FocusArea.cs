namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// A sub-area of a Workstream (e.g. AutoStore, Voice under Automation).
/// May exist without a Workstream.
/// </summary>
public class FocusArea : BaseEntity
{
    public int ProgramId { get; set; }

    public Program? Program { get; set; }

    public int? WorkstreamId { get; set; }

    public Workstream? Workstream { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool Active { get; set; } = true;

    public string? Notes { get; set; }

    public List<Team> Teams { get; set; } = new();

    public List<ResourcePlanLine> ResourcePlanLines { get; set; } = new();
}
