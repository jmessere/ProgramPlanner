namespace WmsResourcePlanner.Application.DTOs;

/// <summary>
/// One row of the Resource Plan monthly grid: a planning context (team,
/// role, person/open-demand, template, phase, workstream) plus its FTE
/// value for each month in the current horizon.
/// </summary>
public class ResourcePlanGridRow
{
    public required ResourcePlanRowKey Key { get; init; }

    public string TeamName { get; init; } = string.Empty;

    public string? TemplateName { get; init; }

    public string? PhaseName { get; init; }

    public string? WorkstreamName { get; init; }

    public string RoleName { get; init; } = string.Empty;

    public string? PersonName { get; init; }

    /// <summary>
    /// Effective sourcing pool name: the assigned Person's own pool when
    /// filled, or the line's proposed sourcing pool when open demand.
    /// </summary>
    public string? PoolName { get; init; }

    public bool IsOpenDemand => Key.PersonId is null;

    public List<MonthlyValue> Months { get; init; } = new();
}
