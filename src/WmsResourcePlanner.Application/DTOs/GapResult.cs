namespace WmsResourcePlanner.Application.DTOs;

public sealed record GapResult(
    int Year,
    int Month,
    string? TemplateName,
    string? WorkstreamName,
    string TeamName,
    string RoleName,
    decimal OpenFte);
