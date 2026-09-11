using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.ResourcePlanning;

public class DataResetServiceTests
{
    [Fact]
    public async Task ClearAllDataAsync_RemovesAllPlanningData_ButKeepsProgramAndCreatesFreshBaselineScenario()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P", StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2031, 12, 31) };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline", IsBaseline = true, Active = true };
        db.PlanningScenarios.Add(scenario);
        var role = new Role { ProgramId = program.Id, Name = "BA" };
        var team = new Team { ProgramId = program.Id, Name = "Team A" };
        var person = new Person { ProgramId = program.Id, FirstName = "Jane", LastName = "Smith", DisplayName = "Jane Smith" };
        var workstream = new Workstream { ProgramId = program.Id, Name = "Inbound" };
        var focusArea = new FocusArea { ProgramId = program.Id, Name = "AutoStore" };
        var site = new Site { ProgramId = program.Id, Name = "DC1" };
        var pool = new ResourcePool { ProgramId = program.Id, Name = "Internal FTE" };
        var template = new Template { ProgramId = program.Id, Name = "Template 1" };
        db.Roles.Add(role);
        db.Teams.Add(team);
        db.People.Add(person);
        db.Workstreams.Add(workstream);
        db.FocusAreas.Add(focusArea);
        db.Sites.Add(site);
        db.ResourcePools.Add(pool);
        db.Templates.Add(template);
        await db.SaveChangesAsync();

        var phase = new TemplatePhase { TemplateId = template.Id, Name = "Design", SortOrder = 1, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31) };
        db.TemplatePhases.Add(phase);
        var assignment = new TeamTemplateAssignment { TeamId = team.Id, TemplateId = template.Id, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 12, 31) };
        db.TeamTemplateAssignments.Add(assignment);
        var teamRole = new TeamRole { TeamId = team.Id, RoleId = role.Id };
        db.TeamRoles.Add(teamRole);
        var siteAssignment = new TeamSiteAssignment { TeamId = team.Id, SiteId = site.Id };
        db.TeamSiteAssignments.Add(siteAssignment);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.Add(new ResourcePlanLine
        {
            ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id, PersonId = person.Id,
            WorkstreamId = workstream.Id, FocusAreaId = focusArea.Id, ResourcePoolId = pool.Id,
            TeamTemplateAssignmentId = assignment.Id, TemplatePhaseId = phase.Id,
            StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 6, 30), Fte = 1.0m
        });
        db.AuditEntries.Add(new AuditEntry { EntityType = "ResourcePlanLine", EntityId = 1, ChangeType = "Create" });
        await db.SaveChangesAsync();

        var sut = new DataResetService(db);
        await sut.ClearAllDataAsync();

        // Program itself (with its planning horizon) is preserved.
        var remainingProgram = Assert.Single(db.Programs.ToList());
        Assert.Equal(program.Id, remainingProgram.Id);
        Assert.Equal(new DateOnly(2027, 1, 1), remainingProgram.StartDate);
        Assert.Equal(new DateOnly(2031, 12, 31), remainingProgram.EndDate);

        // Exactly one fresh baseline scenario remains, everything else is gone.
        var remainingScenario = Assert.Single(db.PlanningScenarios.ToList());
        Assert.True(remainingScenario.IsBaseline);
        Assert.Equal(program.Id, remainingScenario.ProgramId);

        Assert.Empty(db.ResourcePlanLines.ToList());
        Assert.Empty(db.TeamRoles.ToList());
        Assert.Empty(db.TeamSiteAssignments.ToList());
        Assert.Empty(db.TeamTemplateAssignments.ToList());
        Assert.Empty(db.TemplatePhases.ToList());
        Assert.Empty(db.Templates.ToList());
        Assert.Empty(db.Teams.ToList());
        Assert.Empty(db.FocusAreas.ToList());
        Assert.Empty(db.Workstreams.ToList());
        Assert.Empty(db.People.ToList());
        Assert.Empty(db.ResourcePools.ToList());
        Assert.Empty(db.Sites.ToList());
        Assert.Empty(db.Roles.ToList());
        Assert.Empty(db.AuditEntries.ToList());
    }
}
