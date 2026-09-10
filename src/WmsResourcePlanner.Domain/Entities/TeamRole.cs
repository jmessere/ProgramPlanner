namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// Describes typical team composition (not itself time-phased resource demand).
/// </summary>
public class TeamRole : BaseEntity
{
    public int TeamId { get; set; }

    public Team? Team { get; set; }

    public int RoleId { get; set; }

    public Role? Role { get; set; }

    public decimal? DefaultRequiredFte { get; set; }

    public string? Notes { get; set; }
}
