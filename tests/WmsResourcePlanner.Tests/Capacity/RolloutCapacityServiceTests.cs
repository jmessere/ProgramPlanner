using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.Capacity;

public class RolloutCapacityServiceTests
{
    [Fact]
    public async Task GetRolloutCapacityAsync_ReportsAllocatedOpenAndSite()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline" };
        db.PlanningScenarios.Add(scenario);

        var role = new Role { ProgramId = program.Id, Name = "Trainer" };
        db.Roles.Add(role);

        var rolloutTeam = new Team { ProgramId = program.Id, Name = "Site Rollout", TeamType = "Rollout" };
        var otherTeam = new Team { ProgramId = program.Id, Name = "Build", TeamType = "Template Build" };
        db.Teams.AddRange(rolloutTeam, otherTeam);

        var site = new Site { ProgramId = program.Id, Name = "Dallas DC" };
        db.Sites.Add(site);

        var person = new Person { ProgramId = program.Id, FirstName = "A", LastName = "B", DisplayName = "A B", DefaultCapacityFte = 1.0m };
        db.People.Add(person);
        await db.SaveChangesAsync();

        db.TeamSiteAssignments.Add(new TeamSiteAssignment
        {
            TeamId = rolloutTeam.Id,
            SiteId = site.Id,
            StartDate = new DateOnly(2027, 1, 1),
            EndDate = new DateOnly(2027, 12, 31)
        });

        db.ResourcePlanLines.AddRange(
            new ResourcePlanLine { ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = rolloutTeam.Id, RoleId = role.Id, PersonId = person.Id, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 1.0m },
            new ResourcePlanLine { ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = rolloutTeam.Id, RoleId = role.Id, PersonId = null, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 1.0m },
            new ResourcePlanLine { ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = otherTeam.Id, RoleId = role.Id, PersonId = null, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 1.0m });
        await db.SaveChangesAsync();

        var sut = new RolloutCapacityService(db, new ResourceTransformationService());
        var results = await sut.GetRolloutCapacityAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        // Only the Rollout-type team's lines are included.
        Assert.All(results, r => Assert.Equal("Site Rollout", r.TeamName));

        var jan = results.Where(r => r.Year == 2027 && r.Month == 1).ToList();
        Assert.Contains(jan, r => r.PersonName == "A B" && r.AllocatedFte == 1.0m && r.SiteName == "Dallas DC");
        Assert.Contains(jan, r => r.PersonName == null && r.OpenFte == 1.0m);
    }
}
