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
        Assert.Equal("Jane Smith", rpSheet.Cell(2, 7).GetString());
        // Jan column is column 9 (8 fixed headers + 1); value should be 1.0
        Assert.Equal(1.0, rpSheet.Cell(2, 9).GetDouble());
    }

    [Fact]
    public async Task ExportAsync_ResourcePlanSheet_HasAutoFilter()
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
            ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id, PersonId = person.Id,
            StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 6, 30), Fte = 1.0m
        });
        await db.SaveChangesAsync();

        var sut = new ExcelExportService(db, new ResourceTransformationService());
        var bytes = await sut.ExportAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));
        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);

        var rpSheet = workbook.Worksheet("Resource Plan");
        Assert.True(rpSheet.AutoFilter.IsEnabled);
    }

    [Fact]
    public async Task ExportAsync_TemplatePlanSheet_HasNoTeamColumnAndMarksActiveMonths()
    {
        using var db = TestDbFactory.Create();
        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();
        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline", IsBaseline = true };
        db.PlanningScenarios.Add(scenario);
        var template = new Template { ProgramId = program.Id, Name = "Std Rollout", Status = TemplateStatus.Active };
        db.Templates.Add(template);
        await db.SaveChangesAsync();
        var phase = new TemplatePhase
        {
            TemplateId = template.Id, Name = "Design", SortOrder = 1,
            StartDate = new DateOnly(2027, 2, 1), EndDate = new DateOnly(2027, 4, 30)
        };
        db.TemplatePhases.Add(phase);
        await db.SaveChangesAsync();

        var sut = new ExcelExportService(db, new ResourceTransformationService());
        var bytes = await sut.ExportAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));
        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);

        var tpSheet = workbook.Worksheet("Template Plan");
        var headers = Enumerable.Range(1, 3).Select(c => tpSheet.Cell(1, c).GetString()).ToList();
        Assert.DoesNotContain("Team", headers);
        Assert.DoesNotContain("Start Date", headers);
        Assert.DoesNotContain("End Date", headers);
        Assert.Equal(new[] { "Template", "Phase", "Notes" }, headers);

        Assert.Equal("Std Rollout", tpSheet.Cell(2, 1).GetString());
        Assert.Equal("Design", tpSheet.Cell(2, 2).GetString());

        // Month columns start at 4; Feb/Mar/Apr 2027 should be marked "X",
        // Jan and May should not.
        Assert.Equal("", tpSheet.Cell(2, 4).GetString()); // Jan 2027
        Assert.Equal("X", tpSheet.Cell(2, 5).GetString()); // Feb 2027
        Assert.Equal("X", tpSheet.Cell(2, 6).GetString()); // Mar 2027
        Assert.Equal("X", tpSheet.Cell(2, 7).GetString()); // Apr 2027
        Assert.Equal("", tpSheet.Cell(2, 8).GetString()); // May 2027

        Assert.True(tpSheet.AutoFilter.IsEnabled);
        Assert.True(tpSheet.ConditionalFormats.Any());
    }

    [Fact]
    public async Task ExportAsync_SummarySheet_UsesFormulasNotStaticValues()
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
        await db.SaveChangesAsync();
        // Open demand line (no Person) so the "Open FTE by Role/Team" sections have data.
        db.ResourcePlanLines.Add(new ResourcePlanLine
        {
            ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id, PersonId = null,
            StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 6, 30), Fte = 1.0m
        });
        await db.SaveChangesAsync();

        var sut = new ExcelExportService(db, new ResourceTransformationService());
        var bytes = await sut.ExportAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));
        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);

        var summary = workbook.Worksheet("Summary");
        Assert.True(summary.Cell(3, 2).HasFormula, "Filled FTE cell should be a formula.");
        Assert.True(summary.Cell(3, 3).HasFormula, "Open FTE cell should be a formula.");
        Assert.True(summary.Cell(3, 4).HasFormula, "Total Need cell should be a formula.");

        // 12 months (Jan-Dec 2027) starting at column 9 -> Row Total FTE is column 21.
        var rpSheet = workbook.Worksheet("Resource Plan");
        Assert.Equal("Row Total FTE", rpSheet.Cell(1, 21).GetString());
        Assert.True(rpSheet.Cell(2, 21).HasFormula);
    }
}
