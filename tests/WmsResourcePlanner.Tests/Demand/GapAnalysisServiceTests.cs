using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.Demand;

public class GapAnalysisServiceTests
{
    [Fact]
    public async Task GetOpenDemandAsync_OnlyReturnsLinesWithNullPerson()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline" };
        var role = new Role { ProgramId = program.Id, Name = "BA" };
        var team = new Team { ProgramId = program.Id, Name = "Inbound T1", TeamType = "Template Build" };
        db.PlanningScenarios.Add(scenario);
        db.Roles.Add(role);
        db.Teams.Add(team);

        var person = new Person { ProgramId = program.Id, FirstName = "Jane", LastName = "Smith", DisplayName = "Jane Smith" };
        db.People.Add(person);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.AddRange(
            new ResourcePlanLine { ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id, PersonId = null, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 0.5m },
            new ResourcePlanLine { ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id, PersonId = person.Id, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 1.0m });
        await db.SaveChangesAsync();

        var sut = new GapAnalysisService(db, new ResourceTransformationService());

        var results = await sut.GetOpenDemandAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        Assert.Equal(3, results.Count); // Jan, Feb, Mar
        Assert.All(results, r => Assert.Equal(0.5m, r.OpenFte));
        Assert.All(results, r => Assert.Equal("Inbound T1", r.TeamName));
    }
}
