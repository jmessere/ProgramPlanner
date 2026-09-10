namespace WmsResourcePlanner.Application.DTOs;

public enum CapacityStatus
{
    Available,
    FullyAllocated,
    Overallocated
}

public sealed record PersonCapacityResult(
    int PersonId,
    string PersonName,
    int Year,
    int Month,
    decimal CapacityFte,
    decimal AllocatedFte,
    decimal RemainingFte,
    CapacityStatus Status);
