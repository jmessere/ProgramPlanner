using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Infrastructure.Data;

/// <summary>
/// Realistic development seed data: 4 overlapping templates with lifecycle
/// phases, workstreams/focus areas, a mix of dedicated and shared teams,
/// ~30 people, ~10 roles, named allocations, open demand, partial FTE, and
/// at least two intentional overallocations - so major UI behaviors are
/// immediately visible.
/// </summary>
public static class SeedData
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Programs.AnyAsync())
        {
            return;
        }

        var program = new Program
        {
            Name = "WMS Modernization Program",
            Description = "Multi-year, multi-template WMS rollout program.",
            StartDate = new DateOnly(2027, 1, 1),
            EndDate = new DateOnly(2031, 12, 31)
        };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var baseline = new PlanningScenario { ProgramId = program.Id, Name = "Baseline", IsBaseline = true, Active = true };
        var working = new PlanningScenario { ProgramId = program.Id, Name = "Working Plan", IsBaseline = false, Active = true };
        db.PlanningScenarios.AddRange(baseline, working);
        await db.SaveChangesAsync();

        // ---- Workstreams & Focus Areas ----
        string[] workstreamNames = { "Inbound", "Outbound", "Inventory", "Automation", "YMS", "Reporting", "Printing / Labels", "Integrations", "Portal" };
        var workstreams = workstreamNames.Select(n => new Workstream { ProgramId = program.Id, Name = n, Active = true }).ToList();
        db.Workstreams.AddRange(workstreams);
        await db.SaveChangesAsync();

        var automation = workstreams.Single(w => w.Name == "Automation");
        string[] focusAreaNames = { "AutoStore", "Voice", "Conveyors", "Pick & Apply", "Packing" };
        var focusAreas = focusAreaNames.Select(n => new FocusArea { ProgramId = program.Id, WorkstreamId = automation.Id, Name = n, Active = true }).ToList();
        db.FocusAreas.AddRange(focusAreas);
        await db.SaveChangesAsync();

        // ---- Templates & Phases ----
        var templateDefs = new (string Name, DateOnly Start, DateOnly End)[]
        {
            ("Template 1", new DateOnly(2027, 1, 1), new DateOnly(2028, 6, 30)),
            ("Template 2", new DateOnly(2027, 6, 1), new DateOnly(2029, 2, 28)),
            ("Template 3", new DateOnly(2028, 1, 1), new DateOnly(2029, 12, 31)),
            ("Template 4", new DateOnly(2028, 9, 1), new DateOnly(2030, 6, 30)),
        };

        var templates = new List<Template>();
        for (var i = 0; i < templateDefs.Length; i++)
        {
            var (name, start, end) = templateDefs[i];
            templates.Add(new Template { ProgramId = program.Id, Name = name, StartDate = start, EndDate = end, SortOrder = i, Status = TemplateStatus.Active });
        }
        db.Templates.AddRange(templates);
        await db.SaveChangesAsync();

        string[] phaseNames = { "Design", "Build", "Test", "Deployment Preparation", "Rollout", "Hypercare" };
        foreach (var template in templates)
        {
            var totalDays = template.EndDate!.Value.DayNumber - template.StartDate!.Value.DayNumber;
            var segment = totalDays / phaseNames.Length;
            var cursor = template.StartDate.Value;
            for (var i = 0; i < phaseNames.Length; i++)
            {
                var phaseEnd = i == phaseNames.Length - 1 ? template.EndDate.Value : cursor.AddDays(segment);
                db.TemplatePhases.Add(new TemplatePhase
                {
                    TemplateId = template.Id,
                    Name = phaseNames[i],
                    StartDate = cursor,
                    EndDate = phaseEnd,
                    SortOrder = i
                });
                cursor = phaseEnd.AddDays(1);
            }
        }
        await db.SaveChangesAsync();

        // ---- Roles ----
        string[] roleNames = { "Business Analyst", "Product Owner", "Scrum Master", "Developer", "QA", "Solution Architect", "Integration Engineer", "Data Engineer", "Site Lead", "Deployment Lead", "Trainer" };
        var roles = roleNames.Select(n => new Role { ProgramId = program.Id, Name = n, Active = true }).ToList();
        db.Roles.AddRange(roles);
        await db.SaveChangesAsync();
        Role R(string name) => roles.Single(r => r.Name == name);

        // ---- Teams ----
        var inbound = workstreams.Single(w => w.Name == "Inbound");
        var outbound = workstreams.Single(w => w.Name == "Outbound");
        var integrations = workstreams.Single(w => w.Name == "Integrations");

        var teams = new List<Team>
        {
            new() { ProgramId = program.Id, Name = "Inbound T1", TeamType = "Template Build", WorkstreamId = inbound.Id, Active = true },
            new() { ProgramId = program.Id, Name = "Inbound T2", TeamType = "Template Build", WorkstreamId = inbound.Id, Active = true },
            new() { ProgramId = program.Id, Name = "Inbound T3", TeamType = "Template Build", WorkstreamId = inbound.Id, Active = true },
            new() { ProgramId = program.Id, Name = "Outbound T1", TeamType = "Template Build", WorkstreamId = outbound.Id, Active = true },
            new() { ProgramId = program.Id, Name = "Integration Team", TeamType = "Integration", WorkstreamId = integrations.Id, Active = true },
            new() { ProgramId = program.Id, Name = "Testing Team", TeamType = "Testing", Active = true },
            new() { ProgramId = program.Id, Name = "Automation Team", TeamType = "Technical", WorkstreamId = automation.Id, Active = true },
            new() { ProgramId = program.Id, Name = "Rollout Team A", TeamType = "Rollout", Active = true },
            new() { ProgramId = program.Id, Name = "Rollout Team B", TeamType = "Rollout", Active = true },
            new() { ProgramId = program.Id, Name = "Rollout Team C", TeamType = "Rollout", Active = true },
            new() { ProgramId = program.Id, Name = "Rollout Team D", TeamType = "Rollout", Active = true },
        };
        db.Teams.AddRange(teams);
        await db.SaveChangesAsync();
        Team T(string name) => teams.Single(t => t.Name == name);

        // ---- Team <-> Template assignments ----
        var t1 = templates[0]; var t2 = templates[1]; var t3 = templates[2]; var t4 = templates[3];

        var assignments = new List<TeamTemplateAssignment>
        {
            new() { TeamId = T("Inbound T1").Id, TemplateId = t1.Id, StartDate = t1.StartDate!.Value, EndDate = t1.EndDate!.Value },
            new() { TeamId = T("Inbound T2").Id, TemplateId = t2.Id, StartDate = t2.StartDate!.Value, EndDate = t2.EndDate!.Value },
            new() { TeamId = T("Inbound T3").Id, TemplateId = t3.Id, StartDate = t3.StartDate!.Value, EndDate = t3.EndDate!.Value },
            new() { TeamId = T("Outbound T1").Id, TemplateId = t1.Id, StartDate = t1.StartDate!.Value, EndDate = t1.EndDate!.Value },
            // Shared Integration Team supports multiple templates simultaneously.
            new() { TeamId = T("Integration Team").Id, TemplateId = t1.Id, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 12, 31) },
            new() { TeamId = T("Integration Team").Id, TemplateId = t2.Id, StartDate = new DateOnly(2027, 4, 1), EndDate = new DateOnly(2028, 12, 31) },
            new() { TeamId = T("Integration Team").Id, TemplateId = t3.Id, StartDate = new DateOnly(2028, 6, 1), EndDate = new DateOnly(2029, 12, 31) },
            new() { TeamId = T("Testing Team").Id, TemplateId = t1.Id, StartDate = t1.StartDate!.Value, EndDate = t1.EndDate!.Value },
            new() { TeamId = T("Testing Team").Id, TemplateId = t2.Id, StartDate = t2.StartDate!.Value, EndDate = t2.EndDate!.Value },
            new() { TeamId = T("Automation Team").Id, TemplateId = t3.Id, StartDate = t3.StartDate!.Value, EndDate = t3.EndDate!.Value },
            new() { TeamId = T("Automation Team").Id, TemplateId = t4.Id, StartDate = t4.StartDate!.Value, EndDate = t4.EndDate!.Value },
            new() { TeamId = T("Rollout Team A").Id, TemplateId = t1.Id, StartDate = new DateOnly(2027, 10, 1), EndDate = new DateOnly(2028, 6, 30) },
            new() { TeamId = T("Rollout Team B").Id, TemplateId = t2.Id, StartDate = new DateOnly(2028, 6, 1), EndDate = new DateOnly(2029, 2, 28) },
            new() { TeamId = T("Rollout Team C").Id, TemplateId = t3.Id, StartDate = new DateOnly(2029, 4, 1), EndDate = new DateOnly(2029, 12, 31) },
            new() { TeamId = T("Rollout Team D").Id, TemplateId = t4.Id, StartDate = new DateOnly(2029, 12, 1), EndDate = new DateOnly(2030, 6, 30) },
        };
        db.TeamTemplateAssignments.AddRange(assignments);
        await db.SaveChangesAsync();
        TeamTemplateAssignment A(string teamName, int templateId) =>
            assignments.Single(a => a.TeamId == T(teamName).Id && a.TemplateId == templateId);

        // ---- People ----
        string[] firstNames = { "Jane", "Mike", "Sarah", "Mark", "Amy", "Chris", "Dana", "Evan", "Fiona", "George", "Hannah", "Ian", "Julia", "Kevin", "Laura", "Nathan", "Olivia", "Paul", "Quinn", "Rachel", "Sam", "Tara", "Uma", "Victor", "Wendy", "Xavier", "Yara", "Zack", "Beth", "Carl" };
        string[] lastNames = { "Smith", "Jones", "Lee", "Brown", "Patel", "Garcia", "Clark", "Davis", "Miller", "Wilson", "Moore", "Taylor", "Anderson", "Thomas", "Jackson", "White", "Harris", "Martin", "Thompson", "Young", "King", "Wright", "Lopez", "Hill", "Green", "Adams", "Baker", "Nelson", "Carter", "Mitchell" };
        string[] employeeTypes = { "FTE", "Contractor", "Professional Services", "Vendor" };

        var people = new List<Person>();
        for (var i = 0; i < 30; i++)
        {
            var first = firstNames[i];
            var last = lastNames[i];
            people.Add(new Person
            {
                ProgramId = program.Id,
                FirstName = first,
                LastName = last,
                DisplayName = $"{first} {last}",
                PrimaryRoleId = roles[i % roles.Count].Id,
                EmployeeType = employeeTypes[i % employeeTypes.Length],
                DefaultCapacityFte = 1.0m,
                Active = true
            });
        }
        db.People.AddRange(people);
        await db.SaveChangesAsync();
        Person P(string firstName) => people.First(p => p.FirstName == firstName);

        // ---- Resource plan lines (Baseline scenario) ----
        var lines = new List<ResourcePlanLine>
        {
            // Inbound T1 / Template 1: named + open demand, partial FTE.
            Line(program, baseline, T("Inbound T1"), A("Inbound T1", t1.Id), R("Business Analyst"), inbound, P("Jane"), 2027, 1, 2027, 6, 1.0m),
            Line(program, baseline, T("Inbound T1"), A("Inbound T1", t1.Id), R("Business Analyst"), inbound, null, 2027, 1, 2027, 6, 0.5m, "Open BA demand"),
            Line(program, baseline, T("Inbound T1"), A("Inbound T1", t1.Id), R("Developer"), inbound, P("Mike"), 2027, 2, 2027, 9, 1.0m),

            // Inbound T2 / Template 2.
            Line(program, baseline, T("Inbound T2"), A("Inbound T2", t2.Id), R("Business Analyst"), inbound, P("Sarah"), 2027, 8, 2028, 6, 1.0m),
            Line(program, baseline, T("Inbound T2"), A("Inbound T2", t2.Id), R("Developer"), inbound, P("Mark"), 2027, 9, 2028, 8, 1.0m),
            Line(program, baseline, T("Inbound T2"), A("Inbound T2", t2.Id), R("Business Analyst"), inbound, null, 2028, 1, 2028, 4, 0.5m, "Open BA demand"),

            // Inbound T3 / Template 3.
            Line(program, baseline, T("Inbound T3"), A("Inbound T3", t3.Id), R("Business Analyst"), inbound, P("Chris"), 2028, 1, 2028, 12, 1.0m),
            Line(program, baseline, T("Inbound T3"), A("Inbound T3", t3.Id), R("QA"), inbound, null, 2028, 3, 2028, 9, 1.0m, "Open QA demand"),

            // Shared Integration team across two overlapping templates - includes Jane's second, overallocating allocation.
            Line(program, baseline, T("Integration Team"), A("Integration Team", t1.Id), R("Integration Engineer"), integrations, P("Amy"), 2027, 2, 2027, 12, 0.5m),
            Line(program, baseline, T("Integration Team"), A("Integration Team", t2.Id), R("Integration Engineer"), integrations, P("Amy"), 2027, 6, 2028, 3, 0.5m),
            // Jane overallocation #1: already 1.0 FTE on Inbound T1 (Jan-Jun 2027); add Integration 0.5 FTE Apr-Jun 2027 -> 1.5 FTE (150%).
            Line(program, baseline, T("Integration Team"), A("Integration Team", t1.Id), R("Developer"), integrations, P("Jane"), 2027, 4, 2027, 6, 0.5m),

            // Testing Team.
            Line(program, baseline, T("Testing Team"), A("Testing Team", t1.Id), R("QA"), null, P("Dana"), 2027, 3, 2027, 10, 1.0m),
            Line(program, baseline, T("Testing Team"), A("Testing Team", t2.Id), R("QA"), null, P("Evan"), 2028, 1, 2028, 6, 1.0m),

            // Automation Team.
            Line(program, baseline, T("Automation Team"), A("Automation Team", t3.Id), R("Solution Architect"), automation, P("Fiona"), 2028, 2, 2028, 10, 1.0m),
            // Fiona overallocation #2: add second concurrent assignment on Template 4 Automation, Jul-Sep 2028 -> 0.5 FTE more (1.5 total).
            Line(program, baseline, T("Automation Team"), A("Automation Team", t4.Id), R("Solution Architect"), automation, P("Fiona"), 2028, 7, 2028, 9, 0.5m),

            // Rollout teams.
            Line(program, baseline, T("Rollout Team A"), A("Rollout Team A", t1.Id), R("Deployment Lead"), null, P("George"), 2027, 10, 2028, 6, 1.0m),
            Line(program, baseline, T("Rollout Team B"), A("Rollout Team B", t2.Id), R("Site Lead"), null, P("Hannah"), 2028, 6, 2029, 2, 1.0m),
            Line(program, baseline, T("Rollout Team C"), A("Rollout Team C", t3.Id), R("Trainer"), null, null, 2029, 4, 2029, 12, 1.0m, "Open trainer demand"),
            Line(program, baseline, T("Rollout Team D"), A("Rollout Team D", t4.Id), R("Deployment Lead"), null, P("Ian"), 2029, 12, 2030, 6, 1.0m),

            // Outbound.
            Line(program, baseline, T("Outbound T1"), A("Outbound T1", t1.Id), R("Business Analyst"), outbound, P("Julia"), 2027, 1, 2027, 8, 0.75m),
            Line(program, baseline, T("Outbound T1"), A("Outbound T1", t1.Id), R("Developer"), outbound, null, 2027, 3, 2027, 8, 1.0m, "Open developer demand"),
        };

        db.ResourcePlanLines.AddRange(lines);
        await db.SaveChangesAsync();
    }

    private static ResourcePlanLine Line(
        Program program, PlanningScenario scenario, Team team, TeamTemplateAssignment assignment, Role role,
        Workstream? workstream, Person? person, int startYear, int startMonth, int endYear, int endMonth, decimal fte, string? notes = null) => new()
    {
        ProgramId = program.Id,
        ScenarioId = scenario.Id,
        TeamId = team.Id,
        TeamTemplateAssignmentId = assignment.Id,
        WorkstreamId = workstream?.Id,
        RoleId = role.Id,
        PersonId = person?.Id,
        StartDate = new DateOnly(startYear, startMonth, 1),
        EndDate = new DateOnly(endYear, endMonth, DateTime.DaysInMonth(endYear, endMonth)),
        Fte = fte,
        Notes = notes
    };
}
