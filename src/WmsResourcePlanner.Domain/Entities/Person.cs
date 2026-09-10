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
    /// Extensible free-text categorization (FTE, Contractor, Professional
    /// Services, Vendor, TBD, ...).
    /// </summary>
    public string EmployeeType { get; set; } = "FTE";

    public DateOnly? AvailableStartDate { get; set; }

    public DateOnly? AvailableEndDate { get; set; }

    public decimal DefaultCapacityFte { get; set; } = 1.0m;

    public bool Active { get; set; } = true;

    public string? Notes { get; set; }

    public List<ResourcePlanLine> ResourcePlanLines { get; set; } = new();
}
