using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Application.Interfaces;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Resolves parsed Excel import rows against the database (SPEC.md Phases
/// 16-17): a read-only Preview pass that flags which entities are new
/// without writing anything, and a Commit pass that creates/reuses master
/// data via LookupService and writes ResourcePlanLines using the same
/// monthly-expansion/consolidation engine as the grid and Excel export.
/// </summary>
public class ImportService
{
    private readonly IAppDbContext _db;
    private readonly LookupService _lookup;
    private readonly ResourceTransformationService _engine;

    public ImportService(IAppDbContext db, LookupService lookup, ResourceTransformationService engine)
    {
        _db = db;
        _lookup = lookup;
        _engine = engine;
    }

    public async Task<List<ImportPreviewItem>> PreviewAsync(int programId, List<ImportRow> rows, CancellationToken ct = default)
    {
        var existingTeams = await _db.Teams.Where(t => t.ProgramId == programId).Select(t => t.Name.ToLower()).ToListAsync(ct);
        var existingRoles = await _db.Roles.Where(t => t.ProgramId == programId).Select(t => t.Name.ToLower()).ToListAsync(ct);
        var existingPeople = await _db.People.Where(t => t.ProgramId == programId).Select(t => t.DisplayName.ToLower()).ToListAsync(ct);
        var existingWorkstreams = await _db.Workstreams.Where(t => t.ProgramId == programId).Select(t => t.Name.ToLower()).ToListAsync(ct);
        var existingTemplates = await _db.Templates.Where(t => t.ProgramId == programId).Select(t => t.Name.ToLower()).ToListAsync(ct);
        var existingFocusAreas = await _db.FocusAreas.Where(t => t.ProgramId == programId).Select(t => t.Name.ToLower()).ToListAsync(ct);

        var items = new List<ImportPreviewItem>();
        foreach (var row in rows)
        {
            var item = new ImportPreviewItem
            {
                RowNumber = row.RowNumber,
                TeamName = row.TeamName,
                RoleName = row.RoleName,
                PersonName = row.PersonName,
                TemplateName = row.TemplateName,
                WorkstreamName = row.WorkstreamName,
                IsOpenDemand = string.IsNullOrWhiteSpace(row.PersonName),
                Errors = new List<string>(row.Errors),
                IsNewTeam = !string.IsNullOrWhiteSpace(row.TeamName) && !existingTeams.Contains(row.TeamName.Trim().ToLower()),
                IsNewRole = !string.IsNullOrWhiteSpace(row.RoleName) && !existingRoles.Contains(row.RoleName.Trim().ToLower()),
                IsNewPerson = !string.IsNullOrWhiteSpace(row.PersonName) && !existingPeople.Contains(row.PersonName!.Trim().ToLower()),
                IsNewWorkstream = !string.IsNullOrWhiteSpace(row.WorkstreamName) && !existingWorkstreams.Contains(row.WorkstreamName!.Trim().ToLower()),
                IsNewFocusArea = !string.IsNullOrWhiteSpace(row.FocusAreaName) && !existingFocusAreas.Contains(row.FocusAreaName!.Trim().ToLower()),
                IsNewTemplate = !string.IsNullOrWhiteSpace(row.TemplateName) && !existingTemplates.Contains(row.TemplateName!.Trim().ToLower())
            };

            if (row.MonthlyValues.Count == 0)
            {
                item.MonthSummary = "(no FTE values)";
            }
            else
            {
                var ordered = row.MonthlyValues.OrderBy(m => m.Key.Year).ThenBy(m => m.Key.Month).ToList();
                var first = ordered.First();
                var last = ordered.Last();
                item.MonthSummary = $"{new DateOnly(first.Key.Year, first.Key.Month, 1):MMM yyyy} - {new DateOnly(last.Key.Year, last.Key.Month, 1):MMM yyyy} (avg {ordered.Average(m => m.Value):0.##} FTE)";
            }

            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Commits valid rows (those without parse errors) into the given
    /// scenario: resolves/creates master data, ensures a covering
    /// TeamTemplateAssignment exists when a Template is specified, and
    /// replaces any existing ResourcePlanLines for the same row context
    /// with the newly consolidated ranges.
    /// </summary>
    public async Task<int> CommitAsync(int programId, int scenarioId, List<ImportRow> rows, CancellationToken ct = default)
    {
        var committed = 0;

        foreach (var row in rows.Where(r => r.Errors.Count == 0))
        {
            var team = await _lookup.GetOrCreateTeamAsync(programId, row.TeamName, ct: ct);
            var role = await _lookup.GetOrCreateRoleAsync(programId, row.RoleName, ct);
            var person = string.IsNullOrWhiteSpace(row.PersonName)
                ? null
                : await _lookup.GetOrCreatePersonAsync(programId, row.PersonName!, ct);
            var workstream = string.IsNullOrWhiteSpace(row.WorkstreamName)
                ? null
                : await _lookup.GetOrCreateWorkstreamAsync(programId, row.WorkstreamName!, ct);
            var focusArea = string.IsNullOrWhiteSpace(row.FocusAreaName)
                ? null
                : await _lookup.GetOrCreateFocusAreaAsync(programId, row.FocusAreaName!, workstream?.Id, ct);

            int? teamTemplateAssignmentId = null;
            int? templatePhaseId = null;

            if (!string.IsNullOrWhiteSpace(row.TemplateName) && row.MonthlyValues.Count > 0)
            {
                var template = await _lookup.GetOrCreateTemplateAsync(programId, row.TemplateName!, ct);

                var months = row.MonthlyValues.Keys.OrderBy(k => k.Year).ThenBy(k => k.Month).ToList();
                var rowStart = new DateOnly(months.First().Year, months.First().Month, 1);
                var rowEnd = new DateOnly(months.Last().Year, months.Last().Month, 1).AddMonths(1).AddDays(-1);

                var assignment = await _db.TeamTemplateAssignments.FirstOrDefaultAsync(
                    a => a.TeamId == team.Id && a.TemplateId == template.Id
                        && a.StartDate <= rowStart && a.EndDate >= rowEnd, ct);

                if (assignment is null)
                {
                    assignment = new TeamTemplateAssignment
                    {
                        TeamId = team.Id,
                        TemplateId = template.Id,
                        StartDate = rowStart,
                        EndDate = rowEnd
                    };
                    _db.TeamTemplateAssignments.Add(assignment);
                    await _db.SaveChangesAsync(ct);
                }
                teamTemplateAssignmentId = assignment.Id;

                if (!string.IsNullOrWhiteSpace(row.PhaseName))
                {
                    var phase = await _db.TemplatePhases.FirstOrDefaultAsync(
                        p => p.TemplateId == template.Id && p.Name.ToLower() == row.PhaseName!.Trim().ToLower(), ct);
                    templatePhaseId = phase?.Id;
                }
            }

            var key = new ResourcePlanRowKey(
                scenarioId, team.Id, teamTemplateAssignmentId, templatePhaseId,
                workstream?.Id, focusArea?.Id, role.Id, person?.Id);

            var existingLines = await _db.ResourcePlanLines
                .Where(r => r.ScenarioId == key.ScenarioId
                    && r.TeamId == key.TeamId
                    && r.TeamTemplateAssignmentId == key.TeamTemplateAssignmentId
                    && r.TemplatePhaseId == key.TemplatePhaseId
                    && r.WorkstreamId == key.WorkstreamId
                    && r.FocusAreaId == key.FocusAreaId
                    && r.RoleId == key.RoleId
                    && r.PersonId == key.PersonId)
                .ToListAsync(ct);

            foreach (var line in existingLines) _db.ResourcePlanLines.Remove(line);

            var values = row.MonthlyValues
                .Select(kv => new MonthlyValue(kv.Key.Year, kv.Key.Month, kv.Value))
                .OrderBy(v => v.Year).ThenBy(v => v.Month)
                .ToList();
            var ranges = _engine.ConsolidateToRanges(values);

            foreach (var range in ranges)
            {
                _db.ResourcePlanLines.Add(new ResourcePlanLine
                {
                    ProgramId = programId,
                    ScenarioId = key.ScenarioId,
                    TeamId = key.TeamId,
                    TeamTemplateAssignmentId = key.TeamTemplateAssignmentId,
                    TemplatePhaseId = key.TemplatePhaseId,
                    WorkstreamId = key.WorkstreamId,
                    FocusAreaId = key.FocusAreaId,
                    RoleId = key.RoleId,
                    PersonId = key.PersonId,
                    StartDate = range.StartDate,
                    EndDate = range.EndDate,
                    Fte = range.Fte,
                    Notes = row.Notes
                });
            }

            await _db.SaveChangesAsync(ct);
            committed++;
        }

        return committed;
    }
}
