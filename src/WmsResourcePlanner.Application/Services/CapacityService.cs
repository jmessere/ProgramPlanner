using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Application.Interfaces;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Computes per-person, per-month allocation vs. capacity, and flags
/// overallocation.
/// </summary>
public class CapacityService
{
    private readonly IAppDbContext _db;
    private readonly ResourceTransformationService _engine;

    public CapacityService(IAppDbContext db, ResourceTransformationService engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<List<PersonCapacityResult>> GetPersonCapacityAsync(
        int scenarioId, DateOnly horizonStart, DateOnly horizonEnd, int? personId = null, CancellationToken ct = default)
    {
        var query = _db.ResourcePlanLines
            .Where(r => r.ScenarioId == scenarioId && r.PersonId != null);

        if (personId is not null)
        {
            query = query.Where(r => r.PersonId == personId);
        }

        var lines = await query.Include(r => r.Person).ToListAsync(ct);
        var people = await _db.People
            .Where(p => personId == null || p.Id == personId)
            .ToDictionaryAsync(p => p.Id, ct);
        var roleNames = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var monthly = _engine.ExpandToMonthly(lines, horizonStart, horizonEnd);

        // Aggregate across all row-contexts per person/month.
        var perPersonMonth = new Dictionary<(int PersonId, int Year, int Month), decimal>();
        foreach (var (key, values) in monthly)
        {
            if (key.PersonId is null)
            {
                continue;
            }

            foreach (var v in values)
            {
                var mapKey = (key.PersonId.Value, v.Year, v.Month);
                perPersonMonth.TryGetValue(mapKey, out var existing);
                perPersonMonth[mapKey] = existing + v.Fte;
            }
        }

        var results = new List<PersonCapacityResult>();
        foreach (var ((personIdKey, year, month), allocated) in perPersonMonth)
        {
            if (!people.TryGetValue(personIdKey, out var person))
            {
                continue;
            }

            var capacity = person.DefaultCapacityFte;
            var remaining = capacity - allocated;
            var status = allocated > capacity
                ? CapacityStatus.Overallocated
                : allocated == capacity
                    ? CapacityStatus.FullyAllocated
                    : CapacityStatus.Available;

            var roleName = person.PrimaryRoleId is not null && roleNames.TryGetValue(person.PrimaryRoleId.Value, out var rn)
                ? rn
                : string.Empty;

            results.Add(new PersonCapacityResult(
                personIdKey, person.DisplayName, roleName, year, month, capacity, allocated, remaining, status));
        }

        return results
            .OrderBy(r => r.PersonName)
            .ThenBy(r => r.Year)
            .ThenBy(r => r.Month)
            .ToList();
    }
}
