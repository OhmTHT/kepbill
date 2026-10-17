using ReceiptReader.Application.Abstractions;

namespace ReceiptReader.Infrastructure;

public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo Bangkok = FindBangkok();

    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    public DateOnly TodayInThailand => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Bangkok).DateTime);

    private static TimeZoneInfo FindBangkok()
    {
        // IANA id works on Linux/macOS and on Windows with ICU (.NET 6+); fall back to the Windows id, then a fixed +07:00.
        foreach (var id in new[] { "Asia/Bangkok", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "ICT", "ICT");
    }
}
