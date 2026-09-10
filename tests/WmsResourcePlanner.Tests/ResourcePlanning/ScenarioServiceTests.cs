using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.ResourcePlanning;

public class ScenarioServiceTests
{
    [Fact]
    public async Task DuplicateAsync_CopiesResourcePlanLinesButNotMasterData()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var baseline = new PlanningScenario { ProgramId = program.Id, Name = "Baseline", IsBaseline = true };
        var role = new Role { ProgramId = program.Id, Name = "BA" };
        var team = new Team { ProgramId = program.Id, Name = "Inbound T1", TeamType = "Template Build" };
        db.PlanningScenarios.Add(baseline);
        db.Roles.Add(role);
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.Add(new ResourcePlanLine
        {
            ProgramId = program.Id, ScenarioId = baseline.Id, TeamId = team.Id, RoleId = role.Id,
            StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 1.0m
        });
        await db.SaveChangesAsync();

        var sut = new ScenarioService(db);
        var copy = await sut.DuplicateAsync(baseline.Id, "Working Plan");

        Assert.NotEqual(baseline.Id, copy.Id);
        Assert.False(copy.IsBaseline);

        var copiedLines = db.ResourcePlanLines.Where(r => r.ScenarioId == copy.Id).ToList();
        Assert.Single(copiedLines);
        Assert.Equal(1.0m, copiedLines[0].Fte);

        // Master data is shared, not duplicated.
        Assert.Single(db.Teams.ToList());
        Assert.Single(db.Roles.ToList());
    }
}
