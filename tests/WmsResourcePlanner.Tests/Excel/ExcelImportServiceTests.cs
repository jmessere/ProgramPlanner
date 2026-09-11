using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.DTOs;
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
        // The Template Plan table now lives at the top of the merged
        // "Resource Plan" sheet, with month columns starting at column 10
        // (aligned with the Resource Plan table below it): Jan=10, Feb=11,
        // Mar=12, Apr=13, May=14.
        using (var editStream = new MemoryStream(bytes))
        using (var wb = new ClosedXML.Excel.XLWorkbook(editStream))
        {
            var ws = wb.Worksheet("Resource Plan");
            var templateHeaderRow = Enumerable.Range(1, ws.LastRowUsed()!.RowNumber())
                .First(r => ws.Cell(r, 1).GetString() == "Template" && ws.Cell(r, 3).GetString() == "Notes");
            var dataRow = templateHeaderRow + 1;
            Assert.Equal("X", ws.Cell(dataRow, 13).GetString()); // Apr 2027
            ws.Cell(dataRow, 13).Value = string.Empty;
            ws.Cell(dataRow, 14).Value = "X"; // May 2027
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
        var commitResult = await importService.CommitTemplatePlanAsync(program.Id, parsedRows);
        Assert.Equal(1, commitResult.PhasesCommitted);

        var updated = db.TemplatePhases.Single(p => p.Id == phase.Id);
        Assert.Equal(new DateOnly(2027, 2, 1), updated.StartDate);
        Assert.Equal(new DateOnly(2027, 5, 31), updated.EndDate);

        // No duplicate Template/Phase created.
        Assert.Single(db.Templates.ToList());
        Assert.Single(db.TemplatePhases.ToList());
    }

    [Fact]
    public async Task CommitTemplatePlanAsync_RemovesTemplatesAndPhasesNoLongerInWorkbook()
    {
        // The Template Plan table on the workbook is the master/full state:
        // any Template or Phase that already exists in the program but is
        // no longer named in the parsed rows must be deleted, while
        // ResourcePlanLines that referenced the removed phase/template are
        // preserved (just unlinked, per the SetNull FK behavior).
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline", IsBaseline = true };
        db.PlanningScenarios.Add(scenario);
        var team = new Team { ProgramId = program.Id, Name = "Team A" };
        var role = new Role { ProgramId = program.Id, Name = "BA" };
        db.Teams.Add(team);
        db.Roles.Add(role);

        var templateA = new Template { ProgramId = program.Id, Name = "Template A", Status = TemplateStatus.Active };
        var templateB = new Template { ProgramId = program.Id, Name = "Template B", Status = TemplateStatus.Active };
        db.Templates.Add(templateA);
        db.Templates.Add(templateB);
        await db.SaveChangesAsync();

        var designPhaseA = new TemplatePhase { TemplateId = templateA.Id, Name = "Design", SortOrder = 1, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 2, 28) };
        var buildPhaseA = new TemplatePhase { TemplateId = templateA.Id, Name = "Build", SortOrder = 2, StartDate = new DateOnly(2027, 3, 1), EndDate = new DateOnly(2027, 4, 30) };
        var designPhaseB = new TemplatePhase { TemplateId = templateB.Id, Name = "Design", SortOrder = 1, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31) };
        db.TemplatePhases.AddRange(designPhaseA, buildPhaseA, designPhaseB);
        await db.SaveChangesAsync();

        var assignmentB = new TeamTemplateAssignment { TeamId = team.Id, TemplateId = templateB.Id, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 12, 31) };
        db.TeamTemplateAssignments.Add(assignmentB);
        await db.SaveChangesAsync();

        var lineOnTemplateB = new ResourcePlanLine
        {
            ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id,
            TeamTemplateAssignmentId = assignmentB.Id, TemplatePhaseId = designPhaseB.Id,
            StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31), Fte = 1.0m
        };
        db.ResourcePlanLines.Add(lineOnTemplateB);
        await db.SaveChangesAsync();
        var lineId = lineOnTemplateB.Id;

        // Workbook only still lists Template A's Design phase - Template A's
        // Build phase and all of Template B are gone from the sheet.
        var rows = new List<TemplatePlanImportRow>
        {
            new()
            {
                RowNumber = 3, TemplateName = "Template A", PhaseName = "Design",
                StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 2, 28)
            }
        };

        var lookup = new LookupService(db);
        var importService = new ImportService(db, lookup, new ResourceTransformationService());
        var result = await importService.CommitTemplatePlanAsync(program.Id, rows);

        Assert.Equal(1, result.PhasesCommitted);
        Assert.Equal(1, result.TemplatesRemoved); // Template B
        Assert.Equal(1, result.PhasesRemoved); // Template A's Build phase

        Assert.Single(db.Templates.ToList());
        Assert.Equal("Template A", db.Templates.Single().Name);
        Assert.Single(db.TemplatePhases.ToList());
        Assert.Equal("Design", db.TemplatePhases.Single().Name);

        // The line that pointed at the now-deleted Template B phase/assignment
        // survives, just unlinked from that phase/template.
        var survivingLine = db.ResourcePlanLines.Single(l => l.Id == lineId);
        Assert.Null(survivingLine.TemplatePhaseId);
        Assert.Null(survivingLine.TeamTemplateAssignmentId);
        Assert.Equal(team.Id, survivingLine.TeamId);
        Assert.Equal(role.Id, survivingLine.RoleId);
    }

    [Fact]
    public async Task ParseTemplatePlanSheet_RecognizesMonthColumns_WhenHeaderDateTypeIsLostButStyleIsIntact()
    {
        // Regression test for a real-world bug: after a workbook round-trips through Excel,
        // ClosedXML can reload a month-header cell with DataType == Number even though its
        // style still carries a date number format (e.g. built-in format 17, "mmm-yy") and its
        // numeric value is still a valid OLE Automation date serial. Previously this caused
        // FindMonthColumns to find zero month columns, so every phase row failed with
        // "At least one month must be marked with X" regardless of actual X marks present.
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

        using (var editStream = new MemoryStream(bytes))
        using (var wb = new ClosedXML.Excel.XLWorkbook(editStream))
        {
            var ws = wb.Worksheet("Resource Plan");
            var templateHeaderRow = Enumerable.Range(1, ws.LastRowUsed()!.RowNumber())
                .First(r => ws.Cell(r, 1).GetString() == "Template" && ws.Cell(r, 3).GetString() == "Notes");

            // Simulate the corruption: replace the DateTime-typed header cells with a plain
            // numeric value while keeping the same date-formatted style, mirroring what a real
            // Excel save/reload can produce.
            for (var c = 10; c <= 14; c++)
            {
                var headerCell = ws.Cell(templateHeaderRow, c);
                var oaDate = headerCell.GetDateTime().ToOADate();
                headerCell.Style.NumberFormat.NumberFormatId = 17; // built-in "mmm-yy"
                headerCell.Value = oaDate;
            }

            using var savedStream = new MemoryStream();
            wb.SaveAs(savedStream);
            bytes = savedStream.ToArray();
        }

        using var verifyStream = new MemoryStream(bytes);
        using (var wb = new ClosedXML.Excel.XLWorkbook(verifyStream))
        {
            var ws = wb.Worksheet("Resource Plan");
            var templateHeaderRow = Enumerable.Range(1, ws.LastRowUsed()!.RowNumber())
                .First(r => ws.Cell(r, 1).GetString() == "Template" && ws.Cell(r, 3).GetString() == "Notes");
            // Confirm the corrupted state was actually reproduced before asserting the fix.
            Assert.NotEqual(ClosedXML.Excel.XLDataType.DateTime, ws.Cell(templateHeaderRow, 10).DataType);
        }

        using var ms = new MemoryStream(bytes);
        var parsedRows = ExcelImportParser.ParseTemplatePlanSheet(ms);
        Assert.Single(parsedRows);
        Assert.Empty(parsedRows[0].Errors);
        Assert.Equal(new DateOnly(2027, 2, 1), parsedRows[0].StartDate);
        Assert.Equal(new DateOnly(2027, 4, 30), parsedRows[0].EndDate);
    }

    [Fact]
    public async Task ParseReferenceDataSheet_And_CommitReferenceDataAsync_CreatesAndUpdatesEntitiesFromReferenceDataTables()
    {
        using var db = TestDbFactory.Create();

        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline", IsBaseline = true };
        db.PlanningScenarios.Add(scenario);
        var existingPool = new ResourcePool { ProgramId = program.Id, Name = "Acme Contractors", Type = ResourcePoolType.External, AverageRate = 100m };
        db.ResourcePools.Add(existingPool);
        await db.SaveChangesAsync();

        var exportService = new ExcelExportService(db, new ResourceTransformationService());
        var bytes = await exportService.ExportAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30));

        using (var editStream = new MemoryStream(bytes))
        using (var wb = new ClosedXML.Excel.XLWorkbook(editStream))
        {
            var ws = wb.Worksheet("Reference Data");
            var lastCol = ws.LastColumnUsed()!.ColumnNumber();
            int FindCol(string title) => Enumerable.Range(1, lastCol).First(c => ws.Cell(1, c).GetString() == title);

            // Update the existing pool's rate, and add a brand new pool with full details.
            var poolsCol = FindCol("Resource Pools");
            Assert.Equal("Acme Contractors", ws.Cell(3, poolsCol).GetString());
            ws.Cell(3, poolsCol + 3).Value = 125m; // Average Rate
            ws.Cell(4, poolsCol).Value = "New Vendor Pool";
            ws.Cell(4, poolsCol + 1).Value = "External";
            ws.Cell(4, poolsCol + 2).Value = "CC-42";
            ws.Cell(4, poolsCol + 3).Value = 88.5m;
            ws.Cell(4, poolsCol + 4).Value = "Acme Vendor Inc";
            ws.Cell(4, poolsCol + 5).Value = "Added via reference data";

            // Add a new Person that sources from the newly added pool.
            var peopleCol = FindCol("People");
            var firstBlankPersonRow = 3;
            while (!ws.Cell(firstBlankPersonRow, peopleCol).IsEmpty()) firstBlankPersonRow++;
            ws.Cell(firstBlankPersonRow, peopleCol).Value = "New Contractor";
            ws.Cell(firstBlankPersonRow, peopleCol + 1).Value = "New Vendor Pool";
            ws.Cell(firstBlankPersonRow, peopleCol + 2).Value = 0.5m;

            // Add a new Team, Role, Workstream, Focus Area, Site, Template too.
            var teamsCol = FindCol("Teams");
            var r = 3; while (!ws.Cell(r, teamsCol).IsEmpty()) r++;
            ws.Cell(r, teamsCol).Value = "New Team";
            ws.Cell(r, teamsCol + 1).Value = "Functional";

            var rolesCol = FindCol("Roles");
            r = 3; while (!ws.Cell(r, rolesCol).IsEmpty()) r++;
            ws.Cell(r, rolesCol).Value = "New Role";
            ws.Cell(r, rolesCol + 1).Value = "Technical";

            var workstreamsCol = FindCol("Workstreams");
            r = 3; while (!ws.Cell(r, workstreamsCol).IsEmpty()) r++;
            ws.Cell(r, workstreamsCol).Value = "New Workstream";

            var focusAreasCol = FindCol("Focus Areas");
            r = 3; while (!ws.Cell(r, focusAreasCol).IsEmpty()) r++;
            ws.Cell(r, focusAreasCol).Value = "New Focus Area";

            var sitesCol = FindCol("Sites");
            r = 3; while (!ws.Cell(r, sitesCol).IsEmpty()) r++;
            ws.Cell(r, sitesCol).Value = "New Site";
            ws.Cell(r, sitesCol + 1).Value = "EMEA";

            var templatesCol = FindCol("Templates");
            r = 3; while (!ws.Cell(r, templatesCol).IsEmpty()) r++;
            ws.Cell(r, templatesCol).Value = "New Template";
            ws.Cell(r, templatesCol + 1).Value = "Planned";

            using var savedStream = new MemoryStream();
            wb.SaveAs(savedStream);
            bytes = savedStream.ToArray();
        }

        using var ms = new MemoryStream(bytes);
        var refData = ExcelImportParser.ParseReferenceDataSheet(ms);

        Assert.Contains(refData.ResourcePools, p => p.Name == "Acme Contractors" && p.AverageRate == 125m);
        Assert.Contains(refData.ResourcePools, p => p.Name == "New Vendor Pool" && p.CostCenter == "CC-42" && p.Vendor == "Acme Vendor Inc" && p.AverageRate == 88.5m);
        Assert.Contains(refData.People, p => p.Name == "New Contractor" && p.ResourcePoolName == "New Vendor Pool" && p.CapacityFte == 0.5m);
        Assert.Contains(refData.Teams, t => t.Name == "New Team" && t.TeamType == "Functional");
        Assert.Contains(refData.Roles, r => r.Name == "New Role" && r.Category == "Technical");
        Assert.Contains(refData.Workstreams, w => w.Name == "New Workstream");
        Assert.Contains(refData.FocusAreas, f => f.Name == "New Focus Area");
        Assert.Contains(refData.Sites, s => s.Name == "New Site" && s.Region == "EMEA");
        Assert.Contains(refData.Templates, t => t.Name == "New Template" && t.Status == "Planned");

        var lookup = new LookupService(db);
        var importService = new ImportService(db, lookup, new ResourceTransformationService());
        var result = await importService.CommitReferenceDataAsync(program.Id, refData);

        Assert.True(result.TotalProcessed > 0);

        var updatedPool = db.ResourcePools.Single(p => p.Name == "Acme Contractors");
        Assert.Equal(125m, updatedPool.AverageRate);

        var newPool = db.ResourcePools.Single(p => p.Name == "New Vendor Pool");
        Assert.Equal(ResourcePoolType.External, newPool.Type);
        Assert.Equal("CC-42", newPool.CostCenter);
        Assert.Equal(88.5m, newPool.AverageRate);
        Assert.Equal("Acme Vendor Inc", newPool.Vendor);

        var newPerson = db.People.Single(p => p.DisplayName == "New Contractor");
        Assert.Equal(newPool.Id, newPerson.ResourcePoolId);
        Assert.Equal(0.5m, newPerson.DefaultCapacityFte);

        Assert.Contains(db.Teams.Local, t => t.Name == "New Team" && t.TeamType == "Functional");
        Assert.Contains(db.Roles.Local, r => r.Name == "New Role" && r.Category == "Technical");
        Assert.Contains(db.Workstreams.Local, w => w.Name == "New Workstream");
        Assert.Contains(db.FocusAreas.Local, f => f.Name == "New Focus Area");
        Assert.Contains(db.Sites.Local, s => s.Name == "New Site" && s.Region == "EMEA");
        Assert.Contains(db.Templates.Local, t => t.Name == "New Template" && t.Status == TemplateStatus.Planned);
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
