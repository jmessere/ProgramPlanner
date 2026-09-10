using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.Allocation;

public class ResourcePlanLineTests
{
    [Fact]
    public void IsOpenDemand_TrueWhenPersonIdIsNull()
    {
        var line = new ResourcePlanLine { PersonId = null };
        Assert.True(line.IsOpenDemand);
    }

    [Fact]
    public void IsOpenDemand_FalseWhenPersonIdIsSet()
    {
        var line = new ResourcePlanLine { PersonId = 42 };
        Assert.False(line.IsOpenDemand);
    }
}
