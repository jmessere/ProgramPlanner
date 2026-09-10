namespace WmsResourcePlanner.Application.DTOs;

/// <summary>
/// Combined Filled/Open/Total Need FTE for a single Team/Role/Month,
/// supporting the "Filled vs Open vs Total Need" report (Acceptance
/// Scenario D).
/// </summary>
public sealed record FilledOpenTotalResult(
    int Year,
    int Month,
    string TeamName,
    string RoleName,
    decimal FilledFte,
    decimal OpenFte,
    decimal TotalFte);
