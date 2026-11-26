using Microsoft.Extensions.Options;

namespace Cobranca.Api;

/// <summary>Resolves "today" in the business time zone, not in the server's (UTC on Lambda).</summary>
public sealed class Clock(TimeProvider time, IOptions<AppSettings> settings)
{
    private readonly TimeZoneInfo _zone = ResolveZone(settings.Value.TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), _zone).DateTime);

    private static TimeZoneInfo ResolveZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Brazil has no DST since 2019, so a fixed offset is a safe fallback.
            return TimeZoneInfo.CreateCustomTimeZone("UTC-03", TimeSpan.FromHours(-3), "UTC-03", "UTC-03");
        }
    }
}
