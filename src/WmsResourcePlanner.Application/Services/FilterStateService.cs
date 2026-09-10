namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Holds the current filter/grouping selections for filterable pages
/// (currently Global Timeline) so they persist across page navigation
/// within the user's session (SPEC section 57: "filters ... persist during
/// the user's session"). Registered as a scoped (per-circuit) service, so
/// its state lives for the duration of the user's Blazor Server connection
/// and is naturally reset when the session ends (browser reload/close).
/// </summary>
public class FilterStateService
{
    public int? ScenarioId { get; set; }
    public string GroupBy { get; set; } = "Template";

    /// <summary>
    /// Optional second-level grouping applied within each primary group
    /// (e.g. group by Template, then by Team). "None" disables it.
    /// </summary>
    public string GroupBy2 { get; set; } = "None";
    public string TemplateFilter { get; set; } = string.Empty;
    public string WorkstreamFilter { get; set; } = string.Empty;
    public string TeamFilter { get; set; } = string.Empty;
    public string RoleFilter { get; set; } = string.Empty;
    public string PersonFilter { get; set; } = string.Empty;
    public bool OpenDemandOnly { get; set; }
}
