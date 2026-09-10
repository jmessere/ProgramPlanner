using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Domain.Entities;
using WmsResourcePlanner.Infrastructure.Excel;
using Xunit;

namespace WmsResourcePlanner.Tests.Excel;

public class ExcelExportServiceTests
{
    /// <summary>Locates the Resource Plan table's header row within the
    /// merged "Resource Plan" sheet (Template Plan table sits above it).</summary>
    private static int FindResourcePlanHeaderRow(ClosedXML.Excel.IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 1; r <= lastRow; r++)
        {
            if (ws.Cell(r, 1).GetString() == "Template" && ws.Cell(r, 3).GetString() == "Workstream")
            {
                return r;
            }
        }
        throw new InvalidOperationException("Resource Plan header row not found.");
    }

    /// <summary>Locates the Template Plan table's header row within the
    /// merged "Resource Plan" sheet.</summary>
    private static int FindTemplatePlanHeaderRow(ClosedXML.Excel.IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 1; r <= lastRow; r++)
        {
            if (ws.Cell(r, 1).GetString() == "Template" && ws.Cell(r, 3).GetString() == "Notes")
            {
                return r;
            }
        }
        throw new InvalidOperationException("Template Plan header row not found.");
    }

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
        Assert.DoesNotContain("Template Plan", sheetNames); // merged into "Resource Plan"
        Assert.Contains("Reference Data", sheetNames);
        Assert.Contains("Summary", sheetNames);

        var rpSheet = workbook.Worksheet("Resource Plan");
        var headerRow = FindResourcePlanHeaderRow(rpSheet);
        var dataRow = headerRow + 1;
        Assert.Equal("Jane Smith", rpSheet.Cell(dataRow, 7).GetString());
        // Jan column is column 10 (9 fixed headers + 1); value should be 1.0
        Assert.Equal(1.0, rpSheet.Cell(dataRow, 10).GetDouble());
    }

    [Fact]
    public async Task ExportAsync_ResourcePlanSheet_HasFilterableTable()
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

        // Resource Plan is a structured Table (not a plain sheet AutoFilter)
        // so it can be filtered independently from the Template Plan table
        // sharing the same sheet.
        var rpSheet = workbook.Worksheet("Resource Plan");
        var table = rpSheet.Tables.Single(t => t.Name == "ResourcePlanTable");
        Assert.True(table.ShowAutoFilter);
    }

    [Fact]
    public async Task ExportAsync_TemplatePlanTable_HasNoTeamColumnAndMarksActiveMonthsAlignedWithResourcePlan()
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

        // Template Plan is now a table at the top of the merged "Resource Plan" sheet.
        var rpSheet = workbook.Worksheet("Resource Plan");
        var templateHeaderRow = FindTemplatePlanHeaderRow(rpSheet);
        var headers = Enumerable.Range(1, 3).Select(c => rpSheet.Cell(templateHeaderRow, c).GetString()).ToList();
        Assert.DoesNotContain("Team", headers);
        Assert.DoesNotContain("Start Date", headers);
        Assert.DoesNotContain("End Date", headers);
        Assert.Equal(new[] { "Template", "Phase", "Notes" }, headers);

        var dataRow = templateHeaderRow + 1;
        Assert.Equal("Std Rollout", rpSheet.Cell(dataRow, 1).GetString());
        Assert.Equal("Design", rpSheet.Cell(dataRow, 2).GetString());

        // Month columns start at column 10 (RpFirstMonthCol), the same
        // column the Resource Plan table's months start at below, so the
        // two tables' timelines line up. Feb/Mar/Apr 2027 should be marked
        // "X", Jan and May should not.
        Assert.Equal("", rpSheet.Cell(dataRow, 10).GetString()); // Jan 2027
        Assert.Equal("X", rpSheet.Cell(dataRow, 11).GetString()); // Feb 2027
        Assert.Equal("X", rpSheet.Cell(dataRow, 12).GetString()); // Mar 2027
        Assert.Equal("X", rpSheet.Cell(dataRow, 13).GetString()); // Apr 2027
        Assert.Equal("", rpSheet.Cell(dataRow, 14).GetString()); // May 2027

        // Both tables' month columns must be the exact same columns (both
        // remain plain Date-typed cells outside either structured Table,
        // so they keep their real date type/format for re-import parsing).
        var resourcePlanHeaderRow = FindResourcePlanHeaderRow(rpSheet);
        Assert.Equal(rpSheet.Cell(templateHeaderRow, 10).GetDateTime(), rpSheet.Cell(resourcePlanHeaderRow, 10).GetDateTime());

        // Template Plan's label columns (Template/Phase/Notes) form their
        // own structured Table, independently filterable from the
        // Resource Plan table below - the month columns intentionally stay
        // outside the Table (see BuildResourcePlanSheetAsync for why), but
        // an Excel Table's filter hides/shows whole rows regardless, so
        // this still filters the whole Template Plan row including months.
        var templateTable = rpSheet.Tables.Single(t => t.Name == "TemplatePlanTable");
        Assert.True(templateTable.ShowAutoFilter);

        Assert.True(rpSheet.ConditionalFormats.Any());
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

        // 12 months (Jan-Dec 2027) starting at column 10 -> Row Total FTE is column 22.
        var rpSheet = workbook.Worksheet("Resource Plan");
        var headerRow = FindResourcePlanHeaderRow(rpSheet);
        Assert.Equal("Row Total FTE", rpSheet.Cell(headerRow, 22).GetString());
        Assert.True(rpSheet.Cell(headerRow + 1, 22).HasFormula);
    }

    [Fact]
    public async Task ExportAsync_SummarySheet_IncludesPoolAndVendorCostAndCountSections()
    {
        using var db = TestDbFactory.Create();
        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var scenario = new PlanningScenario { ProgramId = program.Id, Name = "Baseline", IsBaseline = true };
        var role = new Role { ProgramId = program.Id, Name = "BA" };
        var team = new Team { ProgramId = program.Id, Name = "Inbound T1", TeamType = "Template Build" };
        var poolA = new ResourcePool { ProgramId = program.Id, Name = "Internal FTE", Type = ResourcePoolType.Internal, AverageRate = 80m, Vendor = null };
        var poolB = new ResourcePool { ProgramId = program.Id, Name = "Acme Contractors", Type = ResourcePoolType.External, AverageRate = 120m, Vendor = "Acme Corp" };
        db.PlanningScenarios.Add(scenario);
        db.Roles.Add(role);
        db.Teams.Add(team);
        db.ResourcePools.AddRange(poolA, poolB);
        var person = new Person { ProgramId = program.Id, FirstName = "Jane", LastName = "Smith", DisplayName = "Jane Smith", ResourcePool = poolA };
        db.People.Add(person);
        await db.SaveChangesAsync();

        db.ResourcePlanLines.AddRange(
            new ResourcePlanLine
            {
                ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id, PersonId = person.Id,
                StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 6, 30), Fte = 1.0m
            },
            new ResourcePlanLine
            {
                ProgramId = program.Id, ScenarioId = scenario.Id, TeamId = team.Id, RoleId = role.Id, PersonId = null,
                ResourcePoolId = poolB.Id,
                StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 6, 30), Fte = 0.5m
            });
        await db.SaveChangesAsync();

        var sut = new ExcelExportService(db, new ResourceTransformationService());
        var bytes = await sut.ExportAsync(scenario.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));
        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);

        var summary = workbook.Worksheet("Summary");
        var allText = string.Join("\n", summary.CellsUsed().Select(c => c.GetString()));

        Assert.Contains("Pool Summary - Cost by Quarter", allText);
        Assert.Contains("Pool Summary - Cost by Year", allText);
        Assert.Contains("Pool Summary - Count by Quarter", allText);
        Assert.Contains("Pool Summary - Count by Year", allText);
        Assert.Contains("Vendor Summary - Cost by Quarter", allText);
        Assert.Contains("Vendor Summary - Cost by Year", allText);
        Assert.Contains("Internal FTE", allText);
        Assert.Contains("Acme Corp", allText);

        // Every non-label data cell in these sections must be a live formula, not a static value.
        var formulaCellCount = summary.CellsUsed(c => c.HasFormula).Count();
        Assert.True(formulaCellCount > 20, "Expected many formula cells across the Filled/Open FTE and Pool/Vendor summary sections.");

        // Resource Plan sheet should carry the new hidden helper columns.
        var rpSheet = workbook.Worksheet("Resource Plan");
        var headerRow = FindResourcePlanHeaderRow(rpSheet);
        var headerRowValues = rpSheet.Row(headerRow).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("Effective Pool", headerRowValues);
        Assert.Contains("Effective Vendor", headerRowValues);
        Assert.Contains("Effective Hourly Rate", headerRowValues);

        // Force ClosedXML's formula engine to evaluate every formula in the
        // workbook so a structurally-broken formula (mismatched parens,
        // bad range, etc.) surfaces as a hard failure here rather than only
        // when a real user opens the file in Excel.
        workbook.RecalculateAllFormulas();
        var errorCells = workbook.Worksheets
            .SelectMany(w => w.CellsUsed(c => c.HasFormula))
            .Where(c => c.CachedValue.IsError)
            .Select(c => $"{c.Worksheet.Name}!{c.Address}: {c.FormulaA1} => {c.CachedValue}")
            .ToList();
        Assert.True(errorCells.Count == 0, "Formula errors found:\n" + string.Join("\n", errorCells));

        // Numeric correctness check: Q1 2027 (Jan-Mar) cost, "Both" variant
        // of the first Pool-Cost-by-Quarter table.
        //   Internal FTE (Jane, 1.0 FTE, $80/hr): 3 months * (2080/12) * 80 = 41,600
        //   Acme Contractors (open demand, 0.5 FTE, $120/hr): 3 * (2080/12) * 0.5 * 120 = 31,200
        var bothSubtitleRow = summary.RowsUsed()
            .First(r => r.Cell(1).GetString() == "Both (Allocated + Demand)")
            .RowNumber();
        var firstDataRow = bothSubtitleRow + 2; // subtitle row, then header row, then data
        var internalFteRow = Enumerable.Range(firstDataRow, 10)
            .Select(r => summary.Row(r))
            .First(r => r.Cell(1).GetString() == "Internal FTE");
        var acmeRow = Enumerable.Range(firstDataRow, 10)
            .Select(r => summary.Row(r))
            .First(r => r.Cell(1).GetString() == "Acme Contractors");

        Assert.Equal(41600d, (double)internalFteRow.Cell(2).CachedValue.GetNumber(), 1);
        Assert.Equal(31200d, (double)acmeRow.Cell(2).CachedValue.GetNumber(), 1);
    }
}
