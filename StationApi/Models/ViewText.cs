using System.Globalization;
using System.Text.Json;

namespace StationApi.Models;
public static class ViewText
{
    public static string Text(this JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "—";
    public static int Number(this JsonElement item, string key) => int.TryParse(item.Text(key), out var value) ? value : 0;
    public static string Time(this JsonElement item, string key) => DateTimeOffset.TryParse(item.Text(key), out var value) ? value.ToString("dd MMM yyyy · HH:mm 'UTC'", CultureInfo.InvariantCulture) : "—";
    public static string Minutes(this JsonElement item, string key) => double.TryParse(item.Text(key), CultureInfo.InvariantCulture, out var value) ? $"{Math.Ceiling(value):N0} min" : "Not predicted";
    public static string Label(string value) => value.Replace('_', ' ').ToLowerInvariant();
}
