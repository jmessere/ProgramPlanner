using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Domain.Entities;
using WmsResourcePlanner.Infrastructure.Excel;
using Xunit;

namespace WmsResourcePlanner.Tests.Excel;

public class ExcelExportServiceTests
{
    [Fact]
    public async Task ExportAsync_ProducesWorkbookWithExpectedSheetsAndData()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline", IsBaseline = true };
        var role = new Role { ProgramId = program.Id, Name = "BA" };
        var team = new Team { ProgramId = program.Id, Name = "Inbound T1", TeamType = "Template Build" };
        db.PlanningScenarios.Add(scenario);
        db.Roles.Add(role);
        db.Teams.Add(team);
        var person = new Person { ProgramId = program.Id, FirstName = "Jane", LastName = "Smith", DisplayName = "Jane Smith" };
        db.People.Add(person);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.Add(new ResourcePlanLine
        {
            ProgramId = program.Id,
            ScenarioId = scenario.Id,
            TeamId = team.Id,
            RoleId = role.Id,
            PersonId = person.Id,
            StartDate = new DateOnly(2027, 1, 1),
            EndDate = new DateOnly(2027, 6, 30),
            Fte = 1.0m
        });
        await db.SaveChangesAsync();

        var sut = new ExcelExportService(db, new ResourceTransformationService());

        var bytes = await sut.ExportAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        Assert.True(bytes.Length > 0);

        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);

        var sheetNames = workbook.Worksheets.Select(w => w.Name).ToList();
        Assert.Contains("Instructions", sheetNames);
        Assert.Contains("Resource Plan", sheetNames);
        Assert.Contains("Template Plan", sheetNames);
        Assert.Contains("Reference Data", sheetNames);
        Assert.Contains("Summary", sheetNames);

        var rpSheet = workbook.Worksheet("Resource Plan");
        Assert.Equal("Jane Smith", rpSheet.Cell(2, 6).GetString());
        // Jan column is column 8 (7 fixed headers + 1); value should be 1.0
        Assert.Equal(1.0, rpSheet.Cell(2, 8).GetDouble());
    }
}
