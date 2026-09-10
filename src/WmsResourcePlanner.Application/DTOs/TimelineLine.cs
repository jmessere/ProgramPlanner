namespace WmsResourcePlanner.Application.DTOs;

/// <summary>
/// A fully-dimensioned resource-plan allocation, used by the Global
/// Timeline view (SPEC.md section 48) which supports grouping and
/// filtering by any of these dimensions.
/// </summary>
public class TimelineLine
{
    public int Id { get; set; }
    public string? TemplateName { get; set; }
    public string? PhaseName { get; set; }
    public string? WorkstreamName { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? PersonName { get; set; }
    public int? PersonId { get; set; }
    public string? EmployeeType { get; set; }
    public string? TeamType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal Fte { get; set; }
    public bool IsOpenDemand => string.IsNullOrEmpty(PersonName);
}
