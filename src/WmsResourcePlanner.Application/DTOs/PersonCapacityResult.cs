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
    string RoleName,
    int Year,
    int Month,
    decimal CapacityFte,
    decimal AllocatedFte,
    decimal RemainingFte,
    CapacityStatus Status);
