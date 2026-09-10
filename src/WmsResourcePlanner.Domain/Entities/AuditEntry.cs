namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// Lightweight audit trail for important planning changes (SPEC.md
/// section 94): resource plan line edits, team-template date changes,
/// template phase date changes, and team date changes.
/// </summary>
public class AuditEntry
{
    public int Id { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public int EntityId { get; set; }

    public string ChangeType { get; set; } = string.Empty;

    public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }
}
