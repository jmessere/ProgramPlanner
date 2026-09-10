using System.Linq;
using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.ResourcePlanning;

public class ResourcePoolTests
{
    [Fact]
    public async Task GetOrCreateResourcePoolAsync_CreatesNewPool_ThenReusesItCaseInsensitively()
    {
        using var db = TestDbFactory.Create();
        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var sut = new LookupService(db);

        var created = await sut.GetOrCreateResourcePoolAsync(program.Id, "Contractor Pool");
        Assert.Equal("Contractor Pool", created.Name);
        Assert.Equal(ResourcePoolType.Internal, created.Type);
        Assert.True(created.Active);

        var reused = await sut.GetOrCreateResourcePoolAsync(program.Id, "contractor pool");
        Assert.Equal(created.Id, reused.Id);

        Assert.Single(db.ResourcePools);
    }

    [Fact]
    public async Task Person_ResourcePoolId_LinksToResourcePool()
    {
        using var db = TestDbFactory.Create();
        var program = new Program { Name = "P" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var pool = new ResourcePool { ProgramId = program.Id, Name = "Internal FTE", Type = ResourcePoolType.Internal, AverageRate = 95m };
        db.ResourcePools.Add(pool);
        await db.SaveChangesAsync();

        var person = new Person { ProgramId = program.Id, FirstName = "Jane", LastName = "Smith", DisplayName = "Jane Smith", ResourcePoolId = pool.Id };
        db.People.Add(person);
        await db.SaveChangesAsync();

        var reloaded = db.People.Single(p => p.Id == person.Id);
        Assert.Equal(pool.Id, reloaded.ResourcePoolId);
    }
}
