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
    public string BarLabel { get; set; } = string.Empty;
    public string? SubLabel { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Color { get; set; }
}
