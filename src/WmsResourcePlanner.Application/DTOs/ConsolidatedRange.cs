using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Application.DTOs;

/// <summary>
/// A consolidated date range with a constant FTE value, produced by
/// collapsing monthly values back into ranges.
/// </summary>
public sealed record ConsolidatedRange(DateOnly StartDate, DateOnly EndDate, decimal Fte);

public static class ResourcePlanRowKeyExtensions
{
    public static ResourcePlanRowKey ToRowKey(this ResourcePlanLine line) => new(
        line.ScenarioId,
        line.TeamId,
        line.TeamTemplateAssignmentId,
        line.TemplatePhaseId,
        line.WorkstreamId,
        line.FocusAreaId,
        line.RoleId,
        line.PersonId);
}
