namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// Time-bound many-to-many relationship between a Team and a Template.
/// A team may support multiple templates simultaneously or at different times.
/// </summary>
public class TeamTemplateAssignment : BaseEntity
{
    public int TeamId { get; set; }

    public Team? Team { get; set; }

    public int TemplateId { get; set; }

    public Template? Template { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string? Notes { get; set; }

    public List<ResourcePlanLine> ResourcePlanLines { get; set; } = new();
}
