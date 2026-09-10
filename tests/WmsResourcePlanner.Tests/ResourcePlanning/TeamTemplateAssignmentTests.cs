using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.ResourcePlanning;

public class TeamTemplateAssignmentTests
{
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
