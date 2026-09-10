namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// A lifecycle phase of a Template (Design, Build, Test, etc.).
/// </summary>
public class TemplatePhase : BaseEntity
{
    public int TemplateId { get; set; }

    public Template? Template { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public int SortOrder { get; set; }

    public string? Notes { get; set; }

    public List<ResourcePlanLine> ResourcePlanLines { get; set; } = new();
}
