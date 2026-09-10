using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.ResourcePlanning;

public class HorizonServiceTests
{
    [Fact]
    public void GetHorizon_WhenNotConfigured_DefaultsToSixtyMonthsFromJanuaryThisYear()
    {
        var sut = new HorizonService();
        var program = new Program { Name = "P" };

        var (start, end) = sut.GetHorizon(program);

        Assert.Equal(new DateOnly(DateTime.Today.Year, 1, 1), start);
        Assert.Equal(60, HorizonService.MonthSpan(start, end));
    }

    [Fact]
    public void GetHorizon_WhenConfigured_UsesProgramStartAndEndDates()
    {
        var sut = new HorizonService();
        var program = new Program
        {
            Name = "P",
            StartDate = new DateOnly(2028, 3, 1),
            EndDate = new DateOnly(2034, 8, 31)
        };

        var (start, end) = sut.GetHorizon(program);

        Assert.Equal(new DateOnly(2028, 3, 1), start);
        Assert.Equal(new DateOnly(2034, 8, 31), end);
        Assert.Equal(78, HorizonService.MonthSpan(start, end));
    }

    [Theory]
    [InlineData(2027, 1, 2027, 1, 1)]
    [InlineData(2027, 1, 2027, 12, 12)]
    [InlineData(2027, 1, 2032, 12, 72)]
    public void MonthSpan_ComputesInclusiveMonthCount(int startYear, int startMonth, int endYear, int endMonth, int expected)
    {
        var start = new DateOnly(startYear, startMonth, 1);
        var end = new DateOnly(endYear, endMonth, 1);

        Assert.Equal(expected, HorizonService.MonthSpan(start, end));
    }
}
