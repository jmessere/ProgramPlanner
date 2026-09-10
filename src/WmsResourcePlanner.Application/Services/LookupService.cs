using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Interfaces;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Centralized "type it, create it if missing" lookup behavior used
/// throughout the app for People, Teams, Roles, Workstreams, Focus Areas,
/// and Sites. Matching is case-insensitive and prevents obvious duplicates.
/// </summary>
public class LookupService
{
    private readonly IAppDbContext _db;

    public LookupService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Team>> SearchTeamsAsync(int programId, string term, CancellationToken ct = default) =>
        await _db.Teams.Where(t => t.ProgramId == programId && t.Name.ToLower().Contains(term.ToLower()))
            .OrderBy(t => t.Name).Take(20).ToListAsync(ct);

    public async Task<Team> GetOrCreateTeamAsync(int programId, string name, string teamType = "Other", CancellationToken ct = default)
    {
        var existing = await _db.Teams.FirstOrDefaultAsync(
            t => t.ProgramId == programId && t.Name.ToLower() == name.Trim().ToLower(), ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = new Team { ProgramId = programId, Name = name.Trim(), TeamType = teamType, Active = true };
        _db.Teams.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<List<Person>> SearchPeopleAsync(int programId, string term, CancellationToken ct = default) =>
        await _db.People.Where(p => p.ProgramId == programId && p.DisplayName.ToLower().Contains(term.ToLower()))
            .OrderBy(p => p.DisplayName).Take(20).ToListAsync(ct);

    public async Task<Person> GetOrCreatePersonAsync(int programId, string displayName, CancellationToken ct = default)
    {
        var existing = await _db.People.FirstOrDefaultAsync(
            p => p.ProgramId == programId && p.DisplayName.ToLower() == displayName.Trim().ToLower(), ct);
        if (existing is not null)
        {
            return existing;
        }

        var parts = displayName.Trim().Split(' ', 2);
        var created = new Person
        {
            ProgramId = programId,
            FirstName = parts[0],
            LastName = parts.Length > 1 ? parts[1] : string.Empty,
            DisplayName = displayName.Trim(),
            EmployeeType = "FTE",
            DefaultCapacityFte = 1.0m,
            Active = true
        };
        _db.People.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<Role> GetOrCreateRoleAsync(int programId, string name, CancellationToken ct = default)
    {
        var existing = await _db.Roles.FirstOrDefaultAsync(
            r => r.ProgramId == programId && r.Name.ToLower() == name.Trim().ToLower(), ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = new Role { ProgramId = programId, Name = name.Trim(), Active = true };
        _db.Roles.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<Workstream> GetOrCreateWorkstreamAsync(int programId, string name, CancellationToken ct = default)
    {
        var existing = await _db.Workstreams.FirstOrDefaultAsync(
            w => w.ProgramId == programId && w.Name.ToLower() == name.Trim().ToLower(), ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = new Workstream { ProgramId = programId, Name = name.Trim(), Active = true };
        _db.Workstreams.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<FocusArea> GetOrCreateFocusAreaAsync(int programId, string name, int? workstreamId, CancellationToken ct = default)
    {
        var existing = await _db.FocusAreas.FirstOrDefaultAsync(
            f => f.ProgramId == programId && f.Name.ToLower() == name.Trim().ToLower(), ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = new FocusArea { ProgramId = programId, Name = name.Trim(), WorkstreamId = workstreamId, Active = true };
        _db.FocusAreas.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<Site> GetOrCreateSiteAsync(int programId, string name, CancellationToken ct = default)
    {
        var existing = await _db.Sites.FirstOrDefaultAsync(
            s => s.ProgramId == programId && s.Name.ToLower() == name.Trim().ToLower(), ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = new Site { ProgramId = programId, Name = name.Trim() };
        _db.Sites.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }
}
