namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// A named individual who can be allocated to resource plan lines.
/// </summary>
public class Person : BaseEntity
{
    public int ProgramId { get; set; }

    public Program? Program { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int? PrimaryRoleId { get; set; }

    public Role? PrimaryRole { get; set; }

    public string? Organization { get; set; }

    public string? Location { get; set; }

    /// <summary>
    /// Where this person is sourced from (replaces the old free-text
    /// EmployeeType). Nullable so existing/imported people can be flagged
    /// as needing a pool assignment rather than silently defaulting.
    /// </summary>
    public int? ResourcePoolId { get; set; }

    public ResourcePool? ResourcePool { get; set; }

    public DateOnly? AvailableStartDate { get; set; }

    public DateOnly? AvailableEndDate { get; set; }

    public decimal DefaultCapacityFte { get; set; } = 1.0m;

    public bool Active { get; set; } = true;

    public string? Notes { get; set; }

    public List<ResourcePlanLine> ResourcePlanLines { get; set; } = new();
}
