namespace WmsResourcePlanner.Domain.Entities;

public class TeamSiteAssignment : BaseEntity
{
    public int TeamId { get; set; }

    public Team? Team { get; set; }

    public int SiteId { get; set; }

    public Site? Site { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }
}
