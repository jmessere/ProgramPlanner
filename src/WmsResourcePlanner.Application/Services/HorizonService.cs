using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Resolves the program-wide planning horizon (the date range shown across
/// the Resource Plan grid, Gantt views, Global Timeline, Reports, and
/// Excel export). Backed by <see cref="Program.StartDate"/> and
/// <see cref="Program.EndDate"/> so it is configurable per SPEC.md's
/// requirement to plan "across 5 years" while supporting at least 60
/// months; when not explicitly configured it defaults to a 60-month
/// window starting January of the current year.
/// </summary>
public class HorizonService
{
    /// <summary>
    /// SPEC.md requires resource planning to support at least 60 months;
    /// this is enforced as a floor whenever the horizon is configured.
    /// </summary>
    public const int MinimumHorizonMonths = 60;

    public const int DefaultHorizonMonths = 60;

    public (DateOnly Start, DateOnly End) GetHorizon(Program program)
    {
        var start = program.StartDate ?? new DateOnly(DateTime.Today.Year, 1, 1);
        var end = program.EndDate ?? start.AddMonths(DefaultHorizonMonths - 1);

        if (end < start)
        {
            end = start;
        }

        return (start, end);
    }

    /// <summary>
    /// Number of whole months spanned by the given start/end (inclusive).
    /// </summary>
    public static int MonthSpan(DateOnly start, DateOnly end)
        => ((end.Year - start.Year) * 12) + (end.Month - start.Month) + 1;
}
