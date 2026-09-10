namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// The core resource-planning entity. Represents either open demand
/// (PersonId == null) or a named allocation (PersonId != null) over a date
/// range, expressed in FTE. Internally stored as date ranges; the monthly
/// grid/Excel matrix are projections produced by the transformation engine.
/// </summary>
public class ResourcePlanLine : BaseEntity
{
    public int ProgramId { get; set; }

    public Program? Program { get; set; }

    public int ScenarioId { get; set; }

    public PlanningScenario? Scenario { get; set; }

    public int TeamId { get; set; }

    public Team? Team { get; set; }

    /// <summary>
    /// Optional link to the specific Team↔Template context this line belongs
    /// to. Resolves which Template a line relates to when a Team supports
    /// several Templates simultaneously.
    /// </summary>
    public int? TeamTemplateAssignmentId { get; set; }

    public TeamTemplateAssignment? TeamTemplateAssignment { get; set; }

    public int? TemplatePhaseId { get; set; }

    public TemplatePhase? TemplatePhase { get; set; }

    public int? WorkstreamId { get; set; }

    public Workstream? Workstream { get; set; }

    public int? FocusAreaId { get; set; }

    public FocusArea? FocusArea { get; set; }

    public int RoleId { get; set; }

    public Role? Role { get; set; }

    /// <summary>
    /// Null means open (unfilled) demand. Populated means a named allocation.
    /// </summary>
    public int? PersonId { get; set; }

    public Person? Person { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public decimal Fte { get; set; }

    public string? Notes { get; set; }

    public bool IsOpenDemand => PersonId is null;
}
