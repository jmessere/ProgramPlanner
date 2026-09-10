namespace WmsResourcePlanner.Application.DTOs;

/// <summary>
/// One month's FTE value for a given planning "row" (team/role/person/etc.)
/// used by the monthly grid and Excel projections.
/// </summary>
public class MonthlyValue
{
    public MonthlyValue(int year, int month, decimal fte)
    {
        Year = year;
        Month = month;
        Fte = fte;
    }

    public int Year { get; init; }

    public int Month { get; init; }

    public decimal Fte { get; init; }

    public DateOnly MonthStart => new(Year, Month, 1);
}
