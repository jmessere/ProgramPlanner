namespace WmsResourcePlanner.Application.DTOs;

/// <summary>
/// All parsed rows from the "Reference Data" sheet's eight master-data
/// tables (People, Teams, Roles, Templates, Workstreams, Focus Areas,
/// Sites, Resource Pools), keyed by table so ImportService can
/// create/update each entity type directly from Reference Data - not just
/// implicitly when a name happens to be referenced on the Resource Plan or
/// Template Plan tables. Blank fields on a row are treated as "clear this
/// field" (the Reference Data sheet is exported as the full current state,
/// so re-importing it is authoritative for these simple attributes).
/// </summary>
public class ReferenceDataImport
{
    public List<ReferencePersonRow> People { get; set; } = new();
    public List<ReferenceTeamRow> Teams { get; set; } = new();
    public List<ReferenceRoleRow> Roles { get; set; } = new();
    public List<ReferenceTemplateRow> Templates { get; set; } = new();
    public List<ReferenceWorkstreamRow> Workstreams { get; set; } = new();
    public List<ReferenceFocusAreaRow> FocusAreas { get; set; } = new();
    public List<ReferenceSiteRow> Sites { get; set; } = new();
    public List<ReferenceResourcePoolRow> ResourcePools { get; set; } = new();
}

public class ReferencePersonRow
{
    public string Name { get; set; } = string.Empty;
    public string? ResourcePoolName { get; set; }
    public decimal? CapacityFte { get; set; }
}

public class ReferenceTeamRow
{
    public string Name { get; set; } = string.Empty;
    public string? TeamType { get; set; }
}

public class ReferenceRoleRow
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
}

public class ReferenceTemplateRow
{
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public class ReferenceWorkstreamRow
{
    public string Name { get; set; } = string.Empty;
}

public class ReferenceFocusAreaRow
{
    public string Name { get; set; } = string.Empty;
}

public class ReferenceSiteRow
{
    public string Name { get; set; } = string.Empty;
    public string? Region { get; set; }
}

public class ReferenceResourcePoolRow
{
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? CostCenter { get; set; }
    public decimal? AverageRate { get; set; }
    public string? Vendor { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Summary counts returned by ImportService.CommitReferenceDataAsync for display.</summary>
public class ReferenceDataImportResult
{
    public int PoolsProcessed { get; set; }
    public int PeopleProcessed { get; set; }
    public int TeamsProcessed { get; set; }
    public int RolesProcessed { get; set; }
    public int TemplatesProcessed { get; set; }
    public int WorkstreamsProcessed { get; set; }
    public int FocusAreasProcessed { get; set; }
    public int SitesProcessed { get; set; }

    public int TotalProcessed =>
        PoolsProcessed + PeopleProcessed + TeamsProcessed + RolesProcessed +
        TemplatesProcessed + WorkstreamsProcessed + FocusAreasProcessed + SitesProcessed;
}
