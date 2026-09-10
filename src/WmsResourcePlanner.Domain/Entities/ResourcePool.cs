namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// Where a person (or a piece of open demand) is sourced from.
/// </summary>
public enum ResourcePoolType
{
    Internal,
    External
}

/// <summary>
/// A Resource Pool describes a sourcing channel for people: it replaces
/// the old free-text Person.EmployeeType and additionally lets open
/// (unfilled) ResourcePlanLine demand indicate where staffing is proposed
/// to come from, via ResourcePlanLine.ResourcePoolId, before any specific
/// Person is named. Multiple pools can share the same Vendor so different
/// sourcing arrangements with one vendor can be rolled up together.
/// </summary>
public class ResourcePool : BaseEntity
{
    public int ProgramId { get; set; }

    public Program? Program { get; set; }

    public string Name { get; set; } = string.Empty;

    public ResourcePoolType Type { get; set; } = ResourcePoolType.Internal;

    public string? CostCenter { get; set; }

    /// <summary>Average hourly rate for people sourced from this pool.</summary>
    public decimal AverageRate { get; set; }

    /// <summary>Optional vendor name, used to roll up multiple pools under one vendor.</summary>
    public string? Vendor { get; set; }

    public string? Notes { get; set; }

    public bool Active { get; set; } = true;

    public List<Person> People { get; set; } = new();

    public List<ResourcePlanLine> ResourcePlanLines { get; set; } = new();
}
