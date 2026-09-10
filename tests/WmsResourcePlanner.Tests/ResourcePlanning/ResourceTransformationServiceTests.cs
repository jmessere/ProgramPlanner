using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Domain.Entities;
using Xunit;

namespace WmsResourcePlanner.Tests.ResourcePlanning;

public class ResourceTransformationServiceTests
{
    private readonly ResourceTransformationService _sut = new();

    private static ResourcePlanLine MakeLine(DateOnly start, DateOnly end, decimal fte, int? personId = 1) => new()
    {
        ScenarioId = 1,
        TeamId = 1,
        RoleId = 1,
        PersonId = personId,
        StartDate = start,
        EndDate = end,
        Fte = fte
    };

    [Fact]
    public void ExpandToMonthly_SplitsDateRangeAcrossMonths()
    {
        var line = MakeLine(new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30), 1.0m);

        var result = _sut.ExpandToMonthly(new[] { line }, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        var key = result.Keys.Single();
        var values = result[key];
        Assert.Equal(12, values.Count);
        Assert.All(values.Take(6), v => Assert.Equal(1.0m, v.Fte));
        Assert.All(values.Skip(6), v => Assert.Equal(0m, v.Fte));
    }

    [Fact]
    public void ExpandToMonthly_SumsOverlappingLinesInSameContext()
    {
        var line1 = MakeLine(new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31), 0.5m);
        var line2 = MakeLine(new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 28), 0.25m);

        var result = _sut.ExpandToMonthly(new[] { line1, line2 }, new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31));

        var values = result.Values.Single();
        Assert.Equal(0.5m, values[0].Fte); // Jan
        Assert.Equal(0.75m, values[1].Fte); // Feb: 0.5 + 0.25
        Assert.Equal(0.5m, values[2].Fte); // Mar
    }

    [Fact]
    public void ExpandToMonthly_HandlesYearBoundary()
    {
        var line = MakeLine(new DateOnly(2027, 11, 1), new DateOnly(2028, 2, 28), 1.0m);

        var result = _sut.ExpandToMonthly(new[] { line }, new DateOnly(2027, 10, 1), new DateOnly(2028, 3, 31));

        var values = result.Values.Single();
        Assert.Equal(new[] { 0m, 1m, 1m, 1m, 1m, 0m }, values.Select(v => v.Fte));
        Assert.Equal(2027, values[1].Year);
        Assert.Equal(11, values[1].Month);
        Assert.Equal(2028, values[3].Year);
        Assert.Equal(1, values[3].Month);
    }

    [Fact]
    public void ConsolidateToRanges_MergesAdjacentIdenticalMonths()
    {
        // Jan=1.0, Feb=1.0, Mar=0.5, Apr=0.5, May=0.5 -> Jan-Feb 1.0, Mar-May 0.5
        var monthly = new List<MonthlyValue>
        {
            new(2027, 1, 1.0m),
            new(2027, 2, 1.0m),
            new(2027, 3, 0.5m),
            new(2027, 4, 0.5m),
            new(2027, 5, 0.5m),
        };

        var ranges = _sut.ConsolidateToRanges(monthly);

        Assert.Equal(2, ranges.Count);
        Assert.Equal(new DateOnly(2027, 1, 1), ranges[0].StartDate);
        Assert.Equal(new DateOnly(2027, 2, 28), ranges[0].EndDate);
        Assert.Equal(1.0m, ranges[0].Fte);
        Assert.Equal(new DateOnly(2027, 3, 1), ranges[1].StartDate);
        Assert.Equal(new DateOnly(2027, 5, 31), ranges[1].EndDate);
        Assert.Equal(0.5m, ranges[1].Fte);
    }

    [Fact]
    public void ConsolidateToRanges_TreatsZeroAsBlankAndBreaksRange()
    {
        var monthly = new List<MonthlyValue>
        {
            new(2027, 1, 1.0m),
            new(2027, 2, 0m),
            new(2027, 3, 1.0m),
        };

        var ranges = _sut.ConsolidateToRanges(monthly);

        Assert.Equal(2, ranges.Count);
        Assert.Equal(new DateOnly(2027, 1, 1), ranges[0].StartDate);
        Assert.Equal(new DateOnly(2027, 1, 31), ranges[0].EndDate);
        Assert.Equal(new DateOnly(2027, 3, 1), ranges[1].StartDate);
        Assert.Equal(new DateOnly(2027, 3, 31), ranges[1].EndDate);
    }

    [Fact]
    public void ConsolidateToRanges_HandlesYearBoundaryAcrossDecemberJanuary()
    {
        var monthly = new List<MonthlyValue>
        {
            new(2027, 12, 0.5m),
            new(2028, 1, 0.5m),
        };

        var ranges = _sut.ConsolidateToRanges(monthly);

        var range = Assert.Single(ranges);
        Assert.Equal(new DateOnly(2027, 12, 1), range.StartDate);
        Assert.Equal(new DateOnly(2028, 1, 31), range.EndDate);
        Assert.Equal(0.5m, range.Fte);
    }

    [Fact]
    public void RoundTrip_ExpandThenConsolidate_ProducesEquivalentRanges()
    {
        var line1 = MakeLine(new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 28), 1.0m);
        var line2 = MakeLine(new DateOnly(2027, 3, 1), new DateOnly(2027, 5, 31), 0.5m);

        var monthly = _sut.ExpandToMonthly(new[] { line1, line2 }, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));
        var ranges = _sut.ConsolidateToRanges(monthly.Values.Single());

        Assert.Equal(2, ranges.Count);
        Assert.Equal(new DateOnly(2027, 1, 1), ranges[0].StartDate);
        Assert.Equal(new DateOnly(2027, 2, 28), ranges[0].EndDate);
        Assert.Equal(1.0m, ranges[0].Fte);
        Assert.Equal(new DateOnly(2027, 3, 1), ranges[1].StartDate);
        Assert.Equal(new DateOnly(2027, 5, 31), ranges[1].EndDate);
        Assert.Equal(0.5m, ranges[1].Fte);
    }

    [Fact]
    public void ExpandToMonthly_SixtyMonthHorizonIsSupported()
    {
        var line = MakeLine(new DateOnly(2027, 1, 1), new DateOnly(2031, 12, 31), 1.0m);

        var result = _sut.ExpandToMonthly(new[] { line }, new DateOnly(2027, 1, 1), new DateOnly(2031, 12, 31));

        Assert.Equal(60, result.Values.Single().Count);
    }
}
