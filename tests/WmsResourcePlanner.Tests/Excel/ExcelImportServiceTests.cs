using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using WmsResourcePlanner.Infrastructure.Excel;
using Xunit;

namespace WmsResourcePlanner.Tests.Excel;

public class ExcelImportServiceTests
{
    [Fact]
    public async Task RoundTrip_ExportThenImport_ReproducesResourcePlanLines()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline", IsBaseline = true };
        var role = new Role { ProgramId = program.Id, Name = "BA" };
        var team = new Team { ProgramId = program.Id, Name = "Inbound T1", TeamType = "Template Build" };
        var person = new Person { ProgramId = program.Id, FirstName = "Jane", LastName = "Smith", DisplayName = "Jane Smith" };
        var workstream = new Workstream { ProgramId = program.Id, Name = "Inbound" };
        var focusArea = new FocusArea { ProgramId = program.Id, Name = "AutoStore" };
        db.PlanningScenarios.Add(scenario);
        db.Roles.Add(role);
        db.Teams.Add(team);
        db.People.Add(person);
        db.Workstreams.Add(workstream);
        db.FocusAreas.Add(focusArea);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.Add(new ResourcePlanLine
        {
            ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id, PersonId = person.Id,
            WorkstreamId = workstream.Id, FocusAreaId = focusArea.Id,
            StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 6, 30), Fte = 1.0m
        });
        await db.SaveChangesAsync();

        var exportService = new ExcelExportService(db, new ResourceTransformationService());
        var bytes = await exportService.ExportAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        // Import into a brand-new scenario in the same program.
        var newScenario = new PlanningScenario { ProgramId = program.Id, Name = "Reimported" };
        db.PlanningScenarios.Add(newScenario);
        await db.SaveChangesAsync();

        using var ms = new MemoryStream(bytes);
        var parsedRows = ExcelImportParser.ParseResourcePlanSheet(ms);
        Assert.Single(parsedRows);
        Assert.Empty(parsedRows[0].Errors);

        var lookup = new LookupService(db);
        var importService = new ImportService(db, lookup, new ResourceTransformationService());
        var count = await importService.CommitAsync(program.Id, newScenario.Id, parsedRows);
        Assert.Equal(1, count);

        var imported = db.ResourcePlanLines.Where(r => r.ScenarioId == newScenario.Id).ToList();
        Assert.Single(imported);
        Assert.Equal(new DateOnly(2027, 1, 1), imported[0].StartDate);
        Assert.Equal(new DateOnly(2027, 6, 30), imported[0].EndDate);
        Assert.Equal(1.0m, imported[0].Fte);
        Assert.Equal(person.Id, imported[0].PersonId);
        Assert.Equal(team.Id, imported[0].TeamId);
        Assert.Equal(workstream.Id, imported[0].WorkstreamId);
        Assert.Equal(focusArea.Id, imported[0].FocusAreaId);

        // No duplicate master data was created for the already-existing team/role/person/workstream/focus area.
        Assert.Single(db.Teams.ToList());
        Assert.Single(db.People.ToList());
        Assert.Single(db.Roles.ToList());
        Assert.Single(db.Workstreams.ToList());
        Assert.Single(db.FocusAreas.ToList());
    }

    [Fact]
    public async Task ParseTemplatePlanSheet_And_CommitTemplatePlanAsync_AdjustsPhaseDatesFromXMarks()
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

        var exportService = new ExcelExportService(db, new ResourceTransformationService());
        var bytes = await exportService.ExportAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        // Simulate the user editing the workbook: remove the "X" from April
        // and add one for May, i.e. shift the phase's end date out a month.
        using (var editStream = new MemoryStream(bytes))
        using (var wb = new ClosedXML.Excel.XLWorkbook(editStream))
        {
            var ws = wb.Worksheet("Template Plan");
            Assert.Equal("X", ws.Cell(2, 9).GetString()); // Apr 2027
            ws.Cell(2, 9).Value = string.Empty;
            ws.Cell(2, 10).Value = "X"; // May 2027
            using var savedStream = new MemoryStream();
            wb.SaveAs(savedStream);
            bytes = savedStream.ToArray();
        }

        using var ms = new MemoryStream(bytes);
        var parsedRows = ExcelImportParser.ParseTemplatePlanSheet(ms);
        Assert.Single(parsedRows);
        Assert.Empty(parsedRows[0].Errors);
        Assert.Equal(new DateOnly(2027, 2, 1), parsedRows[0].StartDate);
        Assert.Equal(new DateOnly(2027, 5, 31), parsedRows[0].EndDate);

        var lookup = new LookupService(db);
        var importService = new ImportService(db, lookup, new ResourceTransformationService());
        var count = await importService.CommitTemplatePlanAsync(program.Id, parsedRows);
        Assert.Equal(1, count);

        var updated = db.TemplatePhases.Single(p => p.Id == phase.Id);
        Assert.Equal(new DateOnly(2027, 2, 1), updated.StartDate);
        Assert.Equal(new DateOnly(2027, 5, 31), updated.EndDate);

        // No duplicate Template/Phase created.
        Assert.Single(db.Templates.ToList());
        Assert.Single(db.TemplatePhases.ToList());
    }

    [Fact]
    public async Task Commit_NewTeamAndPerson_CreatesEntitiesAndOpenOrFilledAllocation()
    {
        // Mirrors SPEC.md Acceptance Scenario I: a workbook user adds a brand new
        // Template/Team/Role/Person combination with monthly FTE values.
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline", IsBaseline = true };
        db.PlanningScenarios.Add(scenario);
        await db.SaveChangesAsync();

        var row = new WmsResourcePlanner.Application.DTOs.ImportRow
        {
            RowNumber = 2,
            TemplateName = "Template 3",
            WorkstreamName = "Inbound",
            TeamName = "New T3 Inbound Team",
            RoleName = "BA",
            PersonName = "Sally Smith",
            MonthlyValues = new()
            {
                [(2027, 2)] = 1.0m,
                [(2027, 3)] = 1.0m,
                [(2027, 4)] = 1.0m
            }
        };

        var lookup = new LookupService(db);
        var importService = new ImportService(db, lookup, new ResourceTransformationService());
        var count = await importService.CommitAsync(program.Id, scenario.Id, new List<WmsResourcePlanner.Application.DTOs.ImportRow> { row });

        Assert.Equal(1, count);
        Assert.NotNull(db.Teams.SingleOrDefault(t => t.Name == "New T3 Inbound Team"));
        Assert.NotNull(db.People.SingleOrDefault(p => p.DisplayName == "Sally Smith"));
        Assert.NotNull(db.Templates.SingleOrDefault(t => t.Name == "Template 3"));
        Assert.NotNull(db.Workstreams.SingleOrDefault(w => w.Name == "Inbound"));

        var line = db.ResourcePlanLines.Single(r => r.ScenarioId == scenario.Id);
        Assert.Equal(new DateOnly(2027, 2, 1), line.StartDate);
        Assert.Equal(new DateOnly(2027, 4, 30), line.EndDate);
        Assert.Equal(1.0m, line.Fte);
        Assert.NotNull(line.TeamTemplateAssignmentId);
    }
}
