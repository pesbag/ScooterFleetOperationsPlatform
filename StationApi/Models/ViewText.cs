using System.Globalization;
using System.Text.Json;

namespace StationApi.Models;
public static class ViewText
{
    public static string Text(this JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "—";
    public static int Number(this JsonElement item, string key) => int.TryParse(item.Text(key), out var value) ? value : 0;
    public static string Time(this JsonElement item, string key) => DateTimeOffset.TryParse(item.Text(key), out var value) ? value.ToUniversalTime().ToString("dd MMM yyyy · HH:mm 'UTC'", CultureInfo.InvariantCulture) : "—";
    public static string Minutes(this JsonElement item, string key) => DurationText.Format(double.TryParse(item.Text(key), CultureInfo.InvariantCulture, out var value) ? value : null);
    public static string StationName(this JsonElement item, string nameKey = "name", string idKey = "id") => string.IsNullOrWhiteSpace(item.Text(nameKey)) || item.Text(nameKey) == "—" ? $"Station #{item.Text(idKey)}" : item.Text(nameKey);
    public static string CapacityText(this JsonElement station) => station.Number("capacity") > 0 ? station.Text("capacity") : "Unknown capacity";
    public static string DataLabel(this JsonElement analysis) => analysis.Text("dataState") switch
    {
        "MISSING" => "Missing data", "OUTDATED" => "Outdated data", "INVALID" => "Invalid data",
        "CURRENT" => analysis.GetProperty("station").Number("count") == 0 ? "Empty station" : "Current data",
        _ => "Unknown data quality"
    };
    public static string ServiceLabel(this JsonElement station, string key) => station.Text(key) switch
    { "True" => "Open", "False" => "Closed", _ => "Unknown" };
    public static string Label(string value) => value.Replace('_', ' ').ToLowerInvariant();
}
