namespace WmsResourcePlanner.Application.DTOs;

/// <summary>
/// One row of the Rollout Capacity report (SPEC.md section 51):
/// allocation/open/capacity/conflict breakdown for Rollout-type teams,
/// sliceable by Template / Rollout Team / Site / Role / Person / Month.
/// </summary>
public sealed record RolloutCapacityResult(
    int Year,
    int Month,
    string? TemplateName,
    string TeamName,
    string? SiteName,
    string RoleName,
    string? PersonName,
    decimal AllocatedFte,
    decimal OpenFte,
    decimal? PersonCapacityFte,
    bool Conflict);
