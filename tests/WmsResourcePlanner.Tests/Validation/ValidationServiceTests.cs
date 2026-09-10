using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.Validation;

public class ValidationServiceTests
{
    [Fact]
    public async Task GetWarningsAsync_FlagsOverallocationAndOutOfBoundsAllocations()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline" };
        var role = new Role { ProgramId = program.Id, Name = "BA" };
        var teamA = new Team { ProgramId = program.Id, Name = "Inbound", TeamType = "Template Build" };
        var teamB = new Team { ProgramId = program.Id, Name = "Integration", TeamType = "Integration" };
        db.PlanningScenarios.Add(scenario);
        db.Roles.Add(role);
        db.Teams.AddRange(teamA, teamB);

        // Jane is only available starting March; her allocation starts in January.
        var jane = new Person
        {
            ProgramId = program.Id, FirstName = "Jane", LastName = "Smith", DisplayName = "Jane Smith",
            DefaultCapacityFte = 1.0m, AvailableStartDate = new DateOnly(2027, 3, 1)
        };
        db.People.Add(jane);
        await db.SaveChangesAsync();

        var template = new Template { ProgramId = program.Id, Name = "Template 1" };
        db.Templates.Add(template);
        await db.SaveChangesAsync();

        // Assignment only covers Feb-Jun, but the line below runs Jan-Jun -> Jan is out of bounds.
        var assignment = new TeamTemplateAssignment
        {
            TeamId = teamA.Id, TemplateId = template.Id,
            StartDate = new DateOnly(2027, 2, 1), EndDate = new DateOnly(2027, 6, 30)
        };
        db.TeamTemplateAssignments.Add(assignment);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.AddRange(
            new ResourcePlanLine { ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = teamA.Id, TeamTemplateAssignmentId = assignment.Id, RoleId = role.Id, PersonId = jane.Id, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 6, 30), Fte = 1.0m },
            new ResourcePlanLine { ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = teamB.Id, RoleId = role.Id, PersonId = jane.Id, StartDate = new DateOnly(2027, 4, 1), EndDate = new DateOnly(2027, 6, 30), Fte = 0.5m });
        await db.SaveChangesAsync();

        var sut = new ValidationService(db, new CapacityService(db, new ResourceTransformationService()));

        var warnings = await sut.GetWarningsAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        // Overallocation in May (1.0 + 0.5 = 1.5 > capacity 1.0).
        Assert.Contains(warnings, w => w.Year == 2027 && w.Month == 5 && w.Message.Contains("overallocated"));

        // Jane's line starts in January but she is not available until March.
        Assert.Contains(warnings, w => w.Year == 2027 && w.Month == 1 && w.Message.Contains("not yet available"));

        // Jane's line starts in January but the team-template assignment starts in February.
        Assert.Contains(warnings, w => w.Year == 2027 && w.Month == 1 && w.Message.Contains("outside the team's assignment"));

        // March is within both availability and assignment bounds and is not overallocated
        // (only the 1.0 FTE Inbound line is active - the Integration line starts in April) -> no warnings.
        Assert.DoesNotContain(warnings, w => w.Year == 2027 && w.Month == 3);
    }
}
