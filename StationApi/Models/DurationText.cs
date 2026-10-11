namespace StationApi.Models;
public static class DurationText
{
    // Round positive fractions up so a positive estimate is never displayed as zero.
    public static string Format(double? minutes, string missing = "Not predicted")
    {
        if (minutes is null || !double.IsFinite(minutes.Value) || minutes < 0 || minutes > int.MaxValue) return missing;
        long total = (long)Math.Ceiling(minutes.Value);
        long hours = total / 60, remainder = total % 60;
        string part(long value, string unit) => $"{value} {unit}{(value == 1 ? "" : "s")}";
        if (hours == 0) return part(remainder, "minute");
        return part(hours, "hour") + (remainder == 0 ? "" : " " + part(remainder, "minute"));
    }
}
