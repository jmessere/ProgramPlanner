namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// A delivery, functional, technical, or rollout team. A Team must NOT carry
/// one authoritative TemplateId - relationships to templates are expressed
/// through TeamTemplateAssignment (many-to-many, time-bound).
/// </summary>
public class Team : BaseEntity
{
    public int ProgramId { get; set; }

    public Program? Program { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Extensible free-text categorization (Template Build, Functional,
    /// Technical, Integration, Testing, Rollout, Hypercare, Program, Other, ...).
    /// </summary>
    public string TeamType { get; set; } = "Other";

    public int? WorkstreamId { get; set; }

    public Workstream? Workstream { get; set; }

    public int? FocusAreaId { get; set; }

    public FocusArea? FocusArea { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool Active { get; set; } = true;

    public string? Notes { get; set; }

    public List<TeamTemplateAssignment> TemplateAssignments { get; set; } = new();

    public List<TeamRole> TeamRoles { get; set; } = new();

    public List<TeamSiteAssignment> SiteAssignments { get; set; } = new();

    public List<ResourcePlanLine> ResourcePlanLines { get; set; } = new();
}
