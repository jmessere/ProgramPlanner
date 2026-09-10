using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.ResourcePlanning;

public class TeamTemplateAssignmentTests
{
    // Regression coverage for the Resource Plan grid's Template dropdown: the
    // dropdown lists Templates (not TeamTemplateAssignments) so option counts
    // are consistent across every row; picking a Template for a row infers or
    // creates the underlying TeamTemplateAssignment behind the scenes.
    [Fact]
    public async Task GetOrCreateTeamTemplateAssignmentAsync_CreatesNewAssignment_WhenTeamHasNoneForTemplate()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var team = new Team { ProgramId = program.Id, Name = "Inbound T1", TeamType = "Template Build" };
        var template = new Template { ProgramId = program.Id, Name = "Template 4" };
        db.Teams.Add(team);
        db.Templates.Add(template);
        await db.SaveChangesAsync();

        var lookup = new LookupService(db);
        var horizonStart = new DateOnly(2027, 1, 1);
        var horizonEnd = new DateOnly(2031, 12, 31);

        Assert.Empty(db.TeamTemplateAssignments.Where(a => a.TeamId == team.Id));

        var created = await lookup.GetOrCreateTeamTemplateAssignmentAsync(team.Id, template.Id, horizonStart, horizonEnd);

        Assert.Equal(team.Id, created.TeamId);
        Assert.Equal(template.Id, created.TemplateId);
        Assert.Equal(horizonStart, created.StartDate);
        Assert.Equal(horizonEnd, created.EndDate);
        Assert.Single(db.TeamTemplateAssignments.Where(a => a.TeamId == team.Id));
    }

    [Fact]
    public async Task GetOrCreateTeamTemplateAssignmentAsync_ReusesExistingAssignment_WhenOneAlreadyExists()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var team = new Team { ProgramId = program.Id, Name = "Automation Team", TeamType = "Automation" };
        var template = new Template { ProgramId = program.Id, Name = "Template 4" };
        db.Teams.Add(team);
        db.Templates.Add(template);
        await db.SaveChangesAsync();

        var existing = new TeamTemplateAssignment
        {
            TeamId = team.Id, TemplateId = template.Id,
            StartDate = new DateOnly(2028, 9, 1), EndDate = new DateOnly(2030, 6, 30)
        };
        db.TeamTemplateAssignments.Add(existing);
        await db.SaveChangesAsync();

        var lookup = new LookupService(db);

        var resolved = await lookup.GetOrCreateTeamTemplateAssignmentAsync(
            team.Id, template.Id, new DateOnly(2027, 1, 1), new DateOnly(2031, 12, 31));

        Assert.Equal(existing.Id, resolved.Id);
        Assert.Single(db.TeamTemplateAssignments.Where(a => a.TeamId == team.Id && a.TemplateId == template.Id));
    }

    [Fact]
    public async Task SharedTeam_CanSupportMultipleTemplatesSimultaneously()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var integrationTeam = new Team { ProgramId = program.Id, Name = "Integration Team", TeamType = "Integration" };
        var template1 = new Template { ProgramId = program.Id, Name = "Template 1" };
        var template2 = new Template { ProgramId = program.Id, Name = "Template 2" };
        db.Teams.Add(integrationTeam);
        db.Templates.AddRange(template1, template2);
        await db.SaveChangesAsync();

        db.TeamTemplateAssignments.AddRange(
            new TeamTemplateAssignment { TeamId = integrationTeam.Id, TemplateId = template1.Id, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 12, 31) },
            new TeamTemplateAssignment { TeamId = integrationTeam.Id, TemplateId = template2.Id, StartDate = new DateOnly(2027, 4, 1), EndDate = new DateOnly(2028, 3, 31) });
        await db.SaveChangesAsync();

        var assignments = db.TeamTemplateAssignments.Where(a => a.TeamId == integrationTeam.Id).ToList();

        Assert.Equal(2, assignments.Count);
        Assert.Contains(assignments, a => a.TemplateId == template1.Id);
        Assert.Contains(assignments, a => a.TemplateId == template2.Id);

        // Overlapping period Apr-Dec 2027 is supported concurrently.
        var overlapStart = new DateOnly(2027, 4, 1);
        var overlapEnd = new DateOnly(2027, 12, 31);
        var concurrent = assignments.Where(a => a.StartDate <= overlapEnd && a.EndDate >= overlapStart).ToList();
        Assert.Equal(2, concurrent.Count);
    }
}
