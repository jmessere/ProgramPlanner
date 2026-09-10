using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.Capacity;

public class CapacityServiceTests
{
    [Fact]
    public async Task GetPersonCapacityAsync_FlagsOverallocatedPerson()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline" };
        db.PlanningScenarios.Add(scenario);

        var role = new Role { ProgramId = program.Id, Name = "BA" };
        db.Roles.Add(role);

        var teamA = new Team { ProgramId = program.Id, Name = "Inbound", TeamType = "Template Build" };
        var teamB = new Team { ProgramId = program.Id, Name = "Integration", TeamType = "Integration" };
        db.Teams.AddRange(teamA, teamB);

        var jane = new Person { ProgramId = program.Id, FirstName = "Jane", LastName = "Smith", DisplayName = "Jane Smith", DefaultCapacityFte = 1.0m };
        db.People.Add(jane);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.AddRange(
            new ResourcePlanLine { ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = teamA.Id, RoleId = role.Id, PersonId = jane.Id, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 6, 30), Fte = 1.0m },
            new ResourcePlanLine { ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = teamB.Id, RoleId = role.Id, PersonId = jane.Id, StartDate = new DateOnly(2027, 4, 1), EndDate = new DateOnly(2027, 6, 30), Fte = 0.5m });
        await db.SaveChangesAsync();

        var sut = new CapacityService(db, new ResourceTransformationService());

        var results = await sut.GetPersonCapacityAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        var may = results.Single(r => r.Year == 2027 && r.Month == 5);
        Assert.Equal(1.5m, may.AllocatedFte);
        Assert.Equal(CapacityStatus.Overallocated, may.Status);

        var february = results.Single(r => r.Year == 2027 && r.Month == 2);
        Assert.Equal(1.0m, february.AllocatedFte);
        Assert.Equal(CapacityStatus.FullyAllocated, february.Status);
    }
}
