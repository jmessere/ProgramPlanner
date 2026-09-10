namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// Top-level program. Initial release assumes one active program, but the
/// model supports multiple programs.
/// </summary>
public class Program : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public List<PlanningScenario> Scenarios { get; set; } = new();

    public List<Template> Templates { get; set; } = new();

    public List<Workstream> Workstreams { get; set; } = new();

    public List<Team> Teams { get; set; } = new();

    public List<Role> Roles { get; set; } = new();

    public List<Person> People { get; set; } = new();

    public List<Site> Sites { get; set; } = new();
}
