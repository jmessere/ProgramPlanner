namespace WmsResourcePlanner.Application.DTOs;

/// <summary>
/// A single draggable/resizable bar in any of the Gantt views. GroupLabel
/// is the row grouping (e.g. Template name, Team name, Person name);
/// BarLabel is the text shown on the bar itself.
/// </summary>
public class GanttBar
{
    public int Id { get; set; }
    public string GroupLabel { get; set; } = string.Empty;

    /// <summary>
    /// Optional second-level grouping label, nested under GroupLabel (e.g.
    /// group by Template, then by Team within each template). Null/empty
    /// means no second-level grouping is active for this bar.
    /// </summary>
    public string? SubGroupLabel { get; set; }
    public string BarLabel { get; set; } = string.Empty;
    public string? SubLabel { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Color { get; set; }
}
