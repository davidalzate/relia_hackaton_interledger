namespace Relia.Api.Services;

/// <summary>Días en America/Bogota: "hoy", el período de una liquidación y los filtros por fecha del API.</summary>
public static class Bogota
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    public static DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    /// <summary>Primer instante del día en Bogotá, en UTC (Npgsql exige offset 0 en timestamptz).</summary>
    public static DateTimeOffset StartOfDay(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
