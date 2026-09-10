using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Interfaces;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Lightweight audit trail (SPEC.md section 94) for resource plan line
/// edits, team-template date changes, template phase date changes, and
/// team date changes.
/// </summary>
public class AuditService
{
    private readonly IAppDbContext _db;

    public AuditService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task RecordAsync(string entityType, int entityId, string changeType, object? oldValue, object? newValue, CancellationToken ct = default)
    {
        _db.AuditEntries.Add(new AuditEntry
        {
            EntityType = entityType,
            EntityId = entityId,
            ChangeType = changeType,
            ChangedUtc = DateTime.UtcNow,
            OldValue = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue)
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<AuditEntry>> GetRecentAsync(int take = 200, CancellationToken ct = default)
    {
        return await _db.AuditEntries
            .OrderByDescending(a => a.ChangedUtc)
            .Take(take)
            .ToListAsync(ct);
    }
}
