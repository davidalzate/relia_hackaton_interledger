namespace Relia.Api.Services.Support;

/// <summary>Fechas de negocio. La base guarda UTC (DateTime con Kind Utc); el día de negocio es America/Bogota.</summary>
public static class Clock
{
    /// <summary>Colombia no tiene horario de verano: UTC-5 fijo.</summary>
    public static readonly TimeSpan BogotaOffset = TimeSpan.FromHours(-5);

    public static DateTime UtcNow(this TimeProvider time) => time.GetUtcNow().UtcDateTime;

    public static DateOnly BogotaToday(this TimeProvider time) =>
        DateOnly.FromDateTime(time.GetUtcNow().ToOffset(BogotaOffset).DateTime);

    public static DateOnly BogotaDate(DateTime utc) =>
        DateOnly.FromDateTime(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(BogotaOffset).DateTime);

    /// <summary>Inicio del día de Bogotá en UTC.</summary>
    public static DateTime StartOfBogotaDayUtc(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), BogotaOffset).UtcDateTime;

    public static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static DateTimeOffset? Utc(DateTime? value) => value is null ? null : Utc(value.Value);
}
