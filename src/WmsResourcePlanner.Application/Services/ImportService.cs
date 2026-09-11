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
        var existingPools = await _db.ResourcePools.Where(t => t.ProgramId == programId).Select(t => t.Name.ToLower()).ToListAsync(ct);

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
                IsNewTemplate = !string.IsNullOrWhiteSpace(row.TemplateName) && !existingTemplates.Contains(row.TemplateName!.Trim().ToLower()),
                IsNewPool = !string.IsNullOrWhiteSpace(row.PoolName) && !existingPools.Contains(row.PoolName!.Trim().ToLower())
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
            var pool = person is null && !string.IsNullOrWhiteSpace(row.PoolName)
                ? await _lookup.GetOrCreateResourcePoolAsync(programId, row.PoolName!, ct)
                : null;
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
                workstream?.Id, focusArea?.Id, role.Id, person?.Id, pool?.Id);

            var existingLines = await _db.ResourcePlanLines
                .Where(r => r.ScenarioId == key.ScenarioId
                    && r.TeamId == key.TeamId
                    && r.TeamTemplateAssignmentId == key.TeamTemplateAssignmentId
                    && r.TemplatePhaseId == key.TemplatePhaseId
                    && r.WorkstreamId == key.WorkstreamId
                    && r.FocusAreaId == key.FocusAreaId
                    && r.RoleId == key.RoleId
                    && r.PersonId == key.PersonId
                    && r.ResourcePoolId == key.ResourcePoolId)
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
                    ResourcePoolId = key.ResourcePoolId,
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

    /// <summary>
    /// Commits valid Template Plan rows: gets-or-creates the Template, then
    /// finds an existing TemplatePhase by (Template, Phase name -
    /// case-insensitive) and updates its dates/notes, or creates a new
    /// phase (appended after the current max SortOrder) if none exists.
    /// This is what makes the "X" re-import workflow adjust dates: the
    /// parser has already derived StartDate/EndDate from the marked
    /// months, so this method just persists whatever it's given.
    ///
    /// The workbook is treated as the master/full state of the Template
    /// Plan: after committing the rows present, any Template or
    /// TemplatePhase that already exists in this program but is no longer
    /// named anywhere in the parsed rows is deleted. Deleting a
    /// TemplatePhase/Template only clears (SetNull) the corresponding
    /// TemplatePhaseId/TeamTemplateAssignmentId on any ResourcePlanLine
    /// that referenced it - the underlying Team/Role/Person/FTE data on
    /// those lines is preserved, only the phase/template linkage is
    /// dropped. Rows with parse errors still count toward "kept" (a
    /// transient X-mark error shouldn't cause an otherwise-known
    /// Template/Phase to be deleted) - only rows with a blank
    /// Template/Phase name are excluded from the kept set.
    /// </summary>
    public async Task<TemplatePlanCommitResult> CommitTemplatePlanAsync(int programId, List<TemplatePlanImportRow> rows, CancellationToken ct = default)
    {
        var result = new TemplatePlanCommitResult();

        foreach (var row in rows.Where(r => r.Errors.Count == 0 && r.StartDate is not null && r.EndDate is not null))
        {
            var template = await _lookup.GetOrCreateTemplateAsync(programId, row.TemplateName, ct);

            var phase = await _db.TemplatePhases.FirstOrDefaultAsync(
                p => p.TemplateId == template.Id && p.Name.ToLower() == row.PhaseName.Trim().ToLower(), ct);

            if (phase is null)
            {
                var maxSortOrder = await _db.TemplatePhases
                    .Where(p => p.TemplateId == template.Id)
                    .Select(p => (int?)p.SortOrder)
                    .MaxAsync(ct) ?? 0;

                phase = new TemplatePhase
                {
                    TemplateId = template.Id,
                    Name = row.PhaseName.Trim(),
                    SortOrder = maxSortOrder + 1,
                    StartDate = row.StartDate!.Value,
                    EndDate = row.EndDate!.Value,
                    Notes = row.Notes
                };
                _db.TemplatePhases.Add(phase);
            }
            else
            {
                phase.StartDate = row.StartDate!.Value;
                phase.EndDate = row.EndDate!.Value;
                phase.Notes = row.Notes;
            }

            await _db.SaveChangesAsync(ct);
            result.PhasesCommitted++;
        }

        var keptTemplateNames = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.TemplateName))
            .Select(r => r.TemplateName.Trim().ToLowerInvariant())
            .ToHashSet();
        var keptPhaseKeys = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.TemplateName) && !string.IsNullOrWhiteSpace(r.PhaseName))
            .Select(r => (Template: r.TemplateName.Trim().ToLowerInvariant(), Phase: r.PhaseName.Trim().ToLowerInvariant()))
            .ToHashSet();

        var existingTemplates = await _db.Templates
            .Where(t => t.ProgramId == programId)
            .Include(t => t.Phases)
            .ToListAsync(ct);

        foreach (var template in existingTemplates)
        {
            var templateNameLower = template.Name.Trim().ToLowerInvariant();
            if (!keptTemplateNames.Contains(templateNameLower))
            {
                // Template no longer named anywhere in the workbook - remove
                // it entirely (cascades to its Phases and TeamTemplateAssignments).
                _db.Templates.Remove(template);
                result.TemplatesRemoved++;
                continue;
            }

            foreach (var phase in template.Phases.ToList())
            {
                var key = (templateNameLower, phase.Name.Trim().ToLowerInvariant());
                if (!keptPhaseKeys.Contains(key))
                {
                    _db.TemplatePhases.Remove(phase);
                    result.PhasesRemoved++;
                }
            }
        }

        if (result.TemplatesRemoved > 0 || result.PhasesRemoved > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return result;
    }

    /// <summary>
    /// Commits all Reference Data sheet rows (SPEC.md Reference Data
    /// import): creates/updates People, Teams, Roles, Templates,
    /// Workstreams, Focus Areas, Sites, and Resource Pools directly from
    /// that sheet's master-data tables, independent of whether any of
    /// those names are also referenced on the Resource Plan or Template
    /// Plan tables. Resource Pools are processed first so a Person row's
    /// ResourcePoolName can resolve against a pool created in the same
    /// import pass.
    /// </summary>
    public async Task<ReferenceDataImportResult> CommitReferenceDataAsync(int programId, ReferenceDataImport data, CancellationToken ct = default)
    {
        var result = new ReferenceDataImportResult();

        foreach (var row in data.ResourcePools)
        {
            var type = !string.IsNullOrWhiteSpace(row.Type) && Enum.TryParse<ResourcePoolType>(row.Type, true, out var parsedType)
                ? parsedType
                : ResourcePoolType.Internal;
            await _lookup.UpsertResourcePoolAsync(programId, row.Name, type, row.CostCenter, row.AverageRate ?? 0m, row.Vendor, row.Notes, ct);
            result.PoolsProcessed++;
        }

        foreach (var row in data.Workstreams)
        {
            await _lookup.UpsertWorkstreamAsync(programId, row.Name, ct);
            result.WorkstreamsProcessed++;
        }

        foreach (var row in data.FocusAreas)
        {
            await _lookup.UpsertFocusAreaAsync(programId, row.Name, ct);
            result.FocusAreasProcessed++;
        }

        foreach (var row in data.Teams)
        {
            await _lookup.UpsertTeamAsync(programId, row.Name, row.TeamType, ct);
            result.TeamsProcessed++;
        }

        foreach (var row in data.Roles)
        {
            await _lookup.UpsertRoleAsync(programId, row.Name, row.Category, ct);
            result.RolesProcessed++;
        }

        foreach (var row in data.Templates)
        {
            await _lookup.UpsertTemplateAsync(programId, row.Name, row.Status, ct);
            result.TemplatesProcessed++;
        }

        foreach (var row in data.Sites)
        {
            await _lookup.UpsertSiteAsync(programId, row.Name, row.Region, ct);
            result.SitesProcessed++;
        }

        foreach (var row in data.People)
        {
            await _lookup.UpsertPersonAsync(programId, row.Name, row.ResourcePoolName, row.CapacityFte ?? 1.0m, ct);
            result.PeopleProcessed++;
        }

        return result;
    }
}
