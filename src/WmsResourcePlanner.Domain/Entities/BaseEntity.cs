namespace WmsResourcePlanner.Domain.Entities;

/// <summary>
/// Common audit fields shared by all domain entities.
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}
