namespace WmsResourcePlanner.Domain.Entities;

public enum TemplateStatus
{
    Planned,
    Active,
    Complete,
    OnHold
}

/// <summary>
/// A WMS deployment template. Templates may overlap and are not assumed to
/// execute sequentially.
/// </summary>
public class Template : BaseEntity
{
    public int ProgramId { get; set; }

    public Program? Program { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public int SortOrder { get; set; }

    public TemplateStatus Status { get; set; } = TemplateStatus.Planned;

    public string? Notes { get; set; }

    public List<TemplatePhase> Phases { get; set; } = new();

    public List<TeamTemplateAssignment> TeamAssignments { get; set; } = new();
}
