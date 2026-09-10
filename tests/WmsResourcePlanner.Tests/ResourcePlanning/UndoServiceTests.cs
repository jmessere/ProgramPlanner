using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.ResourcePlanning;

public class UndoServiceTests
{
    [Fact]
    public async Task UndoAsync_RevertsGridCellEdit()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline" };
        var role = new Role { ProgramId = program.Id, Name = "BA" };
        var team = new Team { ProgramId = program.Id, Name = "Inbound", TeamType = "Template Build" };
        db.PlanningScenarios.Add(scenario);
        db.Roles.Add(role);
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.Add(new ResourcePlanLine
        {
            ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id,
            StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 1.0m
        });
        await db.SaveChangesAsync();

        var undo = new UndoService();
        var sut = new ResourcePlanGridService(db, new ResourceTransformationService(), undo, new AuditService(db));

        var key = new Application.DTOs.ResourcePlanRowKey(scenario.Id, team.Id, null, null, null, null, role.Id, null);
        var horizonStart = new DateOnly(2027, 1, 1);
        var horizonEnd = new DateOnly(2027, 12, 31);

        // Change February's FTE from 1.0 to 0.5.
        await sut.UpdateCellAsync(program.Id, key, horizonStart, horizonEnd, 2027, 2, 0.5m);

        var afterEdit = await sut.GetGridAsync(scenario.Id, horizonStart, horizonEnd);
        var febValue = afterEdit.Single().Months.Single(m => m.Year == 2027 && m.Month == 2).Fte;
        Assert.Equal(0.5m, febValue);

        Assert.True(undo.CanUndo);
        var didUndo = await undo.UndoAsync();
        Assert.True(didUndo);

        var afterUndo = await sut.GetGridAsync(scenario.Id, horizonStart, horizonEnd);
        var febRestored = afterUndo.Single().Months.Single(m => m.Year == 2027 && m.Month == 2).Fte;
        Assert.Equal(1.0m, febRestored);
        Assert.False(undo.CanUndo);
    }
}
