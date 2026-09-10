namespace WmsResourcePlanner.Application.DTOs;

/// <summary>
/// Identifies a single planning "row" (a unique combination of contextual
/// dimensions) whose FTE varies over time as a set of date ranges. Used to
/// group ResourcePlanLine records for monthly expansion/consolidation.
/// </summary>
public sealed record ResourcePlanRowKey(
    int ScenarioId,
    int TeamId,
    int? TeamTemplateAssignmentId,
    int? TemplatePhaseId,
    int? WorkstreamId,
    int? FocusAreaId,
    int RoleId,
    int? PersonId,
    int? ResourcePoolId = null);
