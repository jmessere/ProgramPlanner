namespace WmsResourcePlanner.Application.DTOs;

/// <summary>
/// A single parsed row from the "Resource Plan" sheet of an uploaded
/// workbook, before any database resolution/creation. Blank Person means
/// open demand (SPEC.md convention - no "Record Type" column needed).
/// </summary>
public class ImportRow
{
    public int RowNumber { get; set; }
    public string? TemplateName { get; set; }
    public string? PhaseName { get; set; }
    public string? WorkstreamName { get; set; }
    public string? FocusAreaName { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? PersonName { get; set; }
    public string? Notes { get; set; }

    /// <summary>Year/Month -> FTE, only for months present with a non-blank value.</summary>
    public Dictionary<(int Year, int Month), decimal> MonthlyValues { get; set; } = new();

    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// A read-only preview of what committing an ImportRow will do, computed
/// without writing to the database, so the user can review before
/// committing (SPEC.md Phases 16-17).
/// </summary>
public class ImportPreviewItem
{
    public int RowNumber { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? PersonName { get; set; }
    public string? TemplateName { get; set; }
    public string? WorkstreamName { get; set; }
    public bool IsNewTeam { get; set; }
    public bool IsNewPerson { get; set; }
    public bool IsNewRole { get; set; }
    public bool IsNewWorkstream { get; set; }
    public bool IsNewFocusArea { get; set; }
    public bool IsNewTemplate { get; set; }
    public bool IsOpenDemand { get; set; }
    public string MonthSummary { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public bool HasErrors => Errors.Count > 0;
}
