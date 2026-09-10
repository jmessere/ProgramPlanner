using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.ResourcePlanning;

public class ResourcePlanGridServiceTests
{
    [Fact]
    public async Task UpdateRowContextAsync_MovesLinesToNewRoleAndIsUndoable()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline" };
        var roleA = new Role { ProgramId = program.Id, Name = "BA" };
        var roleB = new Role { ProgramId = program.Id, Name = "PM" };
        var team = new Team { ProgramId = program.Id, Name = "Inbound", TeamType = "Template Build" };
        db.PlanningScenarios.Add(scenario);
        db.Roles.AddRange(roleA, roleB);
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.Add(new ResourcePlanLine
        {
            ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = roleA.Id,
            StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 1.0m
        });
        await db.SaveChangesAsync();

        var undo = new UndoService();
        var sut = new ResourcePlanGridService(db, new ResourceTransformationService(), undo, new AuditService(db));

        var horizonStart = new DateOnly(2027, 1, 1);
        var horizonEnd = new DateOnly(2027, 12, 31);
        var oldKey = new ResourcePlanRowKey(scenario.Id, team.Id, null, null, null, null, roleA.Id, null);

        await sut.UpdateRowContextAsync(oldKey, team.Id, null, null, roleB.Id, null);

        var afterEdit = await sut.GetGridAsync(scenario.Id, horizonStart, horizonEnd);
        var row = Assert.Single(afterEdit);
        Assert.Equal(roleB.Id, row.Key.RoleId);
        Assert.Equal("PM", row.RoleName);

        Assert.True(undo.CanUndo);
        var didUndo = await undo.UndoAsync();
        Assert.True(didUndo);

        var afterUndo = await sut.GetGridAsync(scenario.Id, horizonStart, horizonEnd);
        var restoredRow = Assert.Single(afterUndo);
        Assert.Equal(roleA.Id, restoredRow.Key.RoleId);
        Assert.Equal("BA", restoredRow.RoleName);
    }

    [Fact]
    public async Task UpdateRowContextAsync_MergingIntoExistingRowSumsFte()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline" };
        var roleA = new Role { ProgramId = program.Id, Name = "BA" };
        var roleB = new Role { ProgramId = program.Id, Name = "PM" };
        var team = new Team { ProgramId = program.Id, Name = "Inbound", TeamType = "Template Build" };
        db.PlanningScenarios.Add(scenario);
        db.Roles.AddRange(roleA, roleB);
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.AddRange(
            new ResourcePlanLine
            {
                ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = roleA.Id,
                StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 1.0m
            },
            new ResourcePlanLine
            {
                ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = roleB.Id,
                StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 0.5m
            });
        await db.SaveChangesAsync();

        var sut = new ResourcePlanGridService(db, new ResourceTransformationService(), new UndoService(), new AuditService(db));

        var horizonStart = new DateOnly(2027, 1, 1);
        var horizonEnd = new DateOnly(2027, 12, 31);
        var oldKey = new ResourcePlanRowKey(scenario.Id, team.Id, null, null, null, null, roleA.Id, null);

        // Re-point the roleA row onto roleB: the two rows should merge and
        // sum FTE for the overlapping months, rather than overwrite or lose data.
        await sut.UpdateRowContextAsync(oldKey, team.Id, null, null, roleB.Id, null);

        var rows = await sut.GetGridAsync(scenario.Id, horizonStart, horizonEnd);
        var merged = Assert.Single(rows);
        Assert.Equal(roleB.Id, merged.Key.RoleId);
        var jan = merged.Months.Single(m => m.Year == 2027 && m.Month == 1);
        Assert.Equal(1.5m, jan.Fte);
    }

    [Fact]
    public async Task UpdateCellAsync_CreatesBrandNewRowWhenNoExistingLinesMatchTheKey()
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

        var sut = new ResourcePlanGridService(db, new ResourceTransformationService(), new UndoService(), new AuditService(db));

        var horizonStart = new DateOnly(2027, 1, 1);
        var horizonEnd = new DateOnly(2027, 12, 31);
        var newKey = new ResourcePlanRowKey(scenario.Id, team.Id, null, null, null, null, role.Id, null);

        // No ResourcePlanLine exists yet for this key - simulates typing an
        // FTE value into the Resource Plan grid's always-present blank
        // "new row" for a brand-new Team/Role combination.
        await sut.UpdateCellAsync(program.Id, newKey, horizonStart, horizonEnd, 2027, 3, 0.75m);

        var rows = await sut.GetGridAsync(scenario.Id, horizonStart, horizonEnd);
        var row = Assert.Single(rows);
        Assert.Equal(team.Id, row.Key.TeamId);
        Assert.Equal(role.Id, row.Key.RoleId);
        var march = row.Months.Single(m => m.Year == 2027 && m.Month == 3);
        Assert.Equal(0.75m, march.Fte);
    }

    [Fact]
    public async Task GetGridAsync_OpenDemandRow_ShowsProposedPoolName_And_FilledRow_ShowsPersonsOwnPool()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline" };
        var role = new Role { ProgramId = program.Id, Name = "BA" };
        var team = new Team { ProgramId = program.Id, Name = "Inbound", TeamType = "Template Build" };
        var contractorPool = new ResourcePool { ProgramId = program.Id, Name = "Contractor Pool", Type = ResourcePoolType.External };
        var internalPool = new ResourcePool { ProgramId = program.Id, Name = "Internal FTE", Type = ResourcePoolType.Internal };
        var person = new Person { ProgramId = program.Id, FirstName = "Jane", LastName = "Smith", DisplayName = "Jane Smith", ResourcePool = internalPool };
        db.PlanningScenarios.Add(scenario);
        db.Roles.Add(role);
        db.Teams.Add(team);
        db.ResourcePools.AddRange(contractorPool, internalPool);
        db.People.Add(person);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.AddRange(
            new ResourcePlanLine
            {
                ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id,
                PersonId = null, ResourcePoolId = contractorPool.Id,
                StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 1.0m
            },
            new ResourcePlanLine
            {
                ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id,
                PersonId = person.Id,
                StartDate = new DateOnly(2027, 4, 1), EndDate = new DateOnly(2027, 6, 30), Fte = 1.0m
            });
        await db.SaveChangesAsync();

        var sut = new ResourcePlanGridService(db, new ResourceTransformationService(), new UndoService(), new AuditService(db));
        var rows = await sut.GetGridAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        var openDemandRow = Assert.Single(rows, r => r.IsOpenDemand);
        Assert.Equal("Contractor Pool", openDemandRow.PoolName);
        Assert.Equal(contractorPool.Id, openDemandRow.Key.ResourcePoolId);

        var filledRow = Assert.Single(rows, r => !r.IsOpenDemand);
        Assert.Equal("Internal FTE", filledRow.PoolName);
    }

    [Fact]
    public async Task UpdateRowContextAsync_AssigningPersonClearsOpenDemandPool()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline" };
        var role = new Role { ProgramId = program.Id, Name = "BA" };
        var team = new Team { ProgramId = program.Id, Name = "Inbound", TeamType = "Template Build" };
        var pool = new ResourcePool { ProgramId = program.Id, Name = "Contractor Pool", Type = ResourcePoolType.External };
        var person = new Person { ProgramId = program.Id, FirstName = "Jane", LastName = "Smith", DisplayName = "Jane Smith" };
        db.PlanningScenarios.Add(scenario);
        db.Roles.Add(role);
        db.Teams.Add(team);
        db.ResourcePools.Add(pool);
        db.People.Add(person);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.Add(new ResourcePlanLine
        {
            ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id,
            PersonId = null, ResourcePoolId = pool.Id,
            StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 1.0m
        });
        await db.SaveChangesAsync();

        var sut = new ResourcePlanGridService(db, new ResourceTransformationService(), new UndoService(), new AuditService(db));
        var horizonStart = new DateOnly(2027, 1, 1);
        var horizonEnd = new DateOnly(2027, 12, 31);
        var oldKey = new ResourcePlanRowKey(scenario.Id, team.Id, null, null, null, null, role.Id, null, pool.Id);

        // Naming a Person on a formerly-open-demand row must clear the
        // line's own proposed-sourcing pool: the person's pool is now the
        // effective source.
        await sut.UpdateRowContextAsync(oldKey, team.Id, null, null, role.Id, person.Id, pool.Id);

        var rows = await sut.GetGridAsync(scenario.Id, horizonStart, horizonEnd);
        var row = Assert.Single(rows);
        Assert.Equal(person.Id, row.Key.PersonId);
        Assert.Null(row.Key.ResourcePoolId);
    }
}
