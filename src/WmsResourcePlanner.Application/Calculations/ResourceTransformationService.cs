using WmsResourcePlanner.Application.DTOs;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Application.Calculations;

/// <summary>
/// Converts between date-range ResourcePlanLine records (the persisted
/// representation) and a monthly matrix (the representation used by the
/// grid UI and the Excel workbook). Consolidates adjacent months with an
/// identical FTE value back into a single date range.
/// </summary>
public class ResourceTransformationService
{
    /// <summary>
    /// Expands a set of ResourcePlanLines into a monthly matrix, grouped by
    /// planning row (team/role/person/etc. context). Overlapping lines within
    /// the same row context are summed for a given month. Months outside a
    /// line's date range are omitted (treated as zero/blank).
    /// </summary>
    public Dictionary<ResourcePlanRowKey, List<MonthlyValue>> ExpandToMonthly(
        IEnumerable<ResourcePlanLine> lines,
        DateOnly horizonStart,
        DateOnly horizonEnd)
    {
        var monthCursor = new DateOnly(horizonStart.Year, horizonStart.Month, 1);
        var horizonEndMonth = new DateOnly(horizonEnd.Year, horizonEnd.Month, 1);
        var months = new List<(int Year, int Month)>();
        while (monthCursor <= horizonEndMonth)
        {
            months.Add((monthCursor.Year, monthCursor.Month));
            monthCursor = monthCursor.AddMonths(1);
        }

        var accumulator = new Dictionary<ResourcePlanRowKey, Dictionary<(int Year, int Month), decimal>>();

        foreach (var line in lines)
        {
            var key = line.ToRowKey();
            if (!accumulator.TryGetValue(key, out var monthly))
            {
                monthly = new Dictionary<(int, int), decimal>();
                accumulator[key] = monthly;
            }

            foreach (var (year, month) in months)
            {
                var monthStart = new DateOnly(year, month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                if (line.StartDate > monthEnd || line.EndDate < monthStart)
                {
                    continue;
                }

                monthly.TryGetValue((year, month), out var existing);
                monthly[(year, month)] = existing + line.Fte;
            }
        }

        var result = new Dictionary<ResourcePlanRowKey, List<MonthlyValue>>();
        foreach (var (key, monthly) in accumulator)
        {
            var list = new List<MonthlyValue>();
            foreach (var (year, month) in months)
            {
                monthly.TryGetValue((year, month), out var fte);
                list.Add(new MonthlyValue(year, month, fte));
            }

            result[key] = list;
        }

        return result;
    }

    /// <summary>
    /// Consolidates a chronologically ordered sequence of monthly values into
    /// date ranges, merging adjacent months that share the same FTE value.
    /// Zero-value months are dropped (treated as blank/no line).
    /// </summary>
    public List<ConsolidatedRange> ConsolidateToRanges(IEnumerable<MonthlyValue> monthlyValues)
    {
        var ordered = monthlyValues
            .OrderBy(m => m.Year)
            .ThenBy(m => m.Month)
            .ToList();

        var ranges = new List<ConsolidatedRange>();

        DateOnly? rangeStart = null;
        DateOnly? rangeEnd = null;
        decimal? rangeFte = null;

        foreach (var value in ordered)
        {
            var monthStart = value.MonthStart;
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            if (value.Fte == 0m)
            {
                FlushRange();
                continue;
            }

            if (rangeFte is not null && rangeFte.Value == value.Fte && rangeEnd is not null &&
                monthStart == rangeEnd.Value.AddDays(1))
            {
                // Adjacent month, same value: extend the current range.
                rangeEnd = monthEnd;
                continue;
            }

            // Value changed or gap: close prior range and start a new one.
            FlushRange();
            rangeStart = monthStart;
            rangeEnd = monthEnd;
            rangeFte = value.Fte;
        }

        FlushRange();

        return ranges;

        void FlushRange()
        {
            if (rangeStart is not null && rangeEnd is not null && rangeFte is not null)
            {
                ranges.Add(new ConsolidatedRange(rangeStart.Value, rangeEnd.Value, rangeFte.Value));
            }

            rangeStart = null;
            rangeEnd = null;
            rangeFte = null;
        }
    }
}
