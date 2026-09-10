namespace WmsResourcePlanner.Domain.Entities;

public class Site : BaseEntity
{
    public int ProgramId { get; set; }

    public Program? Program { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? Region { get; set; }

    public DateOnly? PlannedGoLiveDate { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string? Notes { get; set; }

    public List<TeamSiteAssignment> TeamAssignments { get; set; } = new();
}
