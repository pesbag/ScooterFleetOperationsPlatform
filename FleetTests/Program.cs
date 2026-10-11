using StationApi.Services;

// Dependency-free business tests: a failed assertion exits with a non-zero status.
var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
var options = new FleetOptions();
var engine = new SmartFleetEngine(options);
int passed = 0;
void Check(string name, bool success) { if (!success) throw new Exception($"FAIL: {name}"); Console.WriteLine($"PASS: {name}"); passed++; }
StationInput Station(string id, int count, int previous, int capacity = 40) =>
    new(id, id, capacity, count, Math.Max(0, capacity - count), now, true, true, 32, 34,
        [new(now.AddHours(-1), previous), new(now, count)]);
var low = engine.Analyze(Station("low", 4, 10), now);
Check("Outflow is six bikes per hour", low.RatePerHour == -6);
Check("Shortage prediction is forty minutes", Math.Abs(low.ShortageMinutes!.Value - 40) < .001);
Check("Very low utilization is critical", low.RiskScore >= 85 && low.Status == "CRITICAL");
var full = engine.Analyze(Station("full", 37, 31), now);
Check("Overcrowding predicted in thirty minutes", full.FullMinutes == 30 && full.Status == "CRITICAL");
var healthy = engine.Analyze(Station("healthy", 20, 20), now);
Check("Stable balanced station is healthy", healthy.Status == "HEALTHY" && healthy.RiskScore < 40);
Check("Stable station does not invent predictions", healthy.ShortageMinutes is null && healthy.FullMinutes is null);
var stale = engine.Analyze(Station("stale", 3, 15) with { ReportedAt = now.AddHours(-1) }, now);
Check("Stale data suppresses trend and predictions", stale.Stale && stale.RatePerHour is null && stale.ShortageMinutes is null);
var missing = engine.Analyze(Station("missing", 20, 20) with { ReportedAt = null }, now);
Check("Missing data is not labelled healthy", missing.Stale && missing.Status != "HEALTHY");
var noHistory = engine.Analyze(Station("new", 20, 20) with { History = [] }, now);
Check("No history produces no prediction", noHistory.RatePerHour is null);
var duplicate = engine.Analyze(Station("same-time", 20, 20) with { History = [new(now, 20), new(now, 10)] }, now);
Check("Duplicate timestamps do not divide by zero", duplicate.RatePerHour is null);
var zero = engine.Analyze(Station("zero", 0, 0, 0), now);
Check("Unpublished capacity keeps real availability but disables utilization and full forecasts", !zero.Stale && zero.Utilization is null && zero.FullMinutes is null);
Check("Unpublished capacity cannot generate transfers", engine.Recommend([zero, full], new HashSet<string>()).Count == 0);
var invalid = engine.Analyze(Station("invalid", 50, 50), now);
Check("Invalid counts are flagged", invalid.Stale);
var moves = engine.Recommend([low, full], new HashSet<string>());
Check("Rebalancing finds excess station", moves.Count == 1 && moves[0].SourceStationId == "full" && moves[0].DestinationStationId == "low");
Check("Move is bounded by maximum quantity", moves[0].Quantity <= options.MaxMove);
Check("Move respects source inventory", moves[0].Quantity <= full.Station.Count);
Check("Move respects destination capacity", moves[0].Quantity <= low.Station.AvailableDocks);
Check("Reserved station is not allocated twice", engine.Recommend([low, full], new HashSet<string> { "full" }).Count == 0);
Check("Stale source is not used", engine.Recommend([low, stale], new HashSet<string>()).Count == 0);
Check("No self moves", engine.Recommend([low], new HashSet<string>()).Count == 0);
var closed = engine.Analyze(Station("closed", 37, 31) with { Renting = false }, now);
Check("Closed station cannot supply bikes", engine.Recommend([low, closed], new HashSet<string>()).Count == 0);
var noDocks = engine.Analyze(Station("no-docks", 4, 10) with { AvailableDocks = 0 }, now);
Check("Unavailable docks prohibit a move", engine.Recommend([noDocks, full], new HashSet<string>()).Count == 0);
var droppingDonor = engine.Analyze(Station("dropping", 30, 40) with { History = [new(now.AddMinutes(-20), 40), new(now, 30)] }, now);
Check("Donor with imminent shortage is excluded", engine.Recommend([low, droppingDonor], new HashSet<string>()).Count == 0);
var secondLow = engine.Analyze(Station("low2", 2, 10), now);
var grouped = engine.Recommend([low, secondLow, full], new HashSet<string>());
Check("One active move per station", grouped.SelectMany(x => new[] { x.SourceStationId, x.DestinationStationId }).Distinct().Count() == grouped.Count * 2);
Check("Invalid thresholds rejected", !(new FleetOptions { TargetRatio = .99 }).IsValid());
Check("Empty station predicts immediate shortage", engine.Analyze(Station("empty", 0, 0), now).ShortageMinutes == 0);
Check("Full station predicts immediate overflow", engine.Analyze(Station("capacity", 40, 40), now).FullMinutes == 0);
var absentCount = engine.Analyze(Station("absent", 0, 0) with { Count = null }, now);
Check("Missing count is not an empty station", absentCount.DataState == "MISSING" && absentCount.ShortageMinutes is null && absentCount.Utilization is null);
Check("Missing receiver never generates a move", engine.Recommend([absentCount, full], new HashSet<string>()).Count == 0);
var absentDocks = engine.Analyze(Station("absent-docks", 4, 10) with { AvailableDocks = null }, now);
Check("Missing return capacity prevents recommendations", absentDocks.DataState == "MISSING" && engine.Recommend([absentDocks, full], new HashSet<string>()).Count == 0);
var absentService = engine.Analyze(Station("absent-service", 4, 10) with { Renting = null }, now);
Check("Unknown rental state prevents recommendations", absentService.Stale && engine.Recommend([absentService, full], new HashSet<string>()).Count == 0);
var staleEmpty = engine.Analyze(Station("old-empty", 0, 0) with { ReportedAt = now.AddHours(-1), ReceivedAt = now }, now);
Check("New receipt does not make an old empty reading current", staleEmpty.DataState == "OUTDATED" && staleEmpty.ShortageMinutes is null && staleEmpty.Status == "WARNING");
var future = engine.Analyze(Station("future", 0, 0) with { ReportedAt = now.AddMinutes(3) }, now);
Check("Future source clock is invalid", future.DataState == "INVALID" && future.ShortageMinutes is null);
Check("Fresh zero remains a real empty station", engine.Analyze(Station("real-empty", 0, 0), now) is { DataState: "CURRENT", ShortageMinutes: 0 });
Check("Excessive return capacity is invalid", engine.Analyze(Station("bad-docks", 4, 10) with { AvailableDocks = 99 }, now).DataState == "INVALID");
var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var absentDto = System.Text.Json.JsonSerializer.Deserialize<Bike_fleet_operation_platform.Dtos.StationStatusDto>("{\"station_id\":\"missing\"}", jsonOptions)!;
Check("Ingestion preserves absent fields as null", absentDto.Num_bikes_available is null && absentDto.Num_docs_available is null && absentDto.Is_renting is null);
var explicitNullDto = System.Text.Json.JsonSerializer.Deserialize<Bike_fleet_operation_platform.Dtos.StationStatusDto>("{\"station_id\":\"null\",\"num_bikes_available\":null,\"last_reported\":null}", jsonOptions)!;
Check("Explicit null source timestamp remains missing", explicitNullDto.Num_bikes_available is null && explicitNullDto.Last_reported is null);
var zeroDto = System.Text.Json.JsonSerializer.Deserialize<Bike_fleet_operation_platform.Dtos.StationStatusDto>("{\"station_id\":\"zero\",\"num_bikes_available\":0,\"num_docks_available\":0}", jsonOptions)!;
Check("Ingestion preserves explicit zero", zeroDto.Num_bikes_available == 0 && zeroDto.Num_docs_available == 0);
absentDto.ReceivedAt = now;
var processedDto = System.Text.Json.JsonSerializer.Deserialize<ProcessingService.Dtos.StationStatusDto>(System.Text.Json.JsonSerializer.Serialize(absentDto), jsonOptions)!;
Check("Kafka JSON round trip retains missing count and receipt time", processedDto.Num_bikes_available is null && processedDto.ReceivedAt == now);
var flags = System.Text.Json.JsonSerializer.Deserialize<Bike_fleet_operation_platform.Dtos.StationStatusDto>("{\"is_renting\":true,\"is_returning\":false}", jsonOptions)!;
Check("GBFS boolean flags convert to existing Kafka integers", flags.Is_renting == 1 && flags.Is_returning == 0);
if (args.Contains("--gbfs"))
{
    using var http = new HttpClient { BaseAddress = new Uri("https://gbfs.lyft.com/gbfs/2.3/bkn/en/"), Timeout = TimeSpan.FromSeconds(25) };
    using var infoFeed = await Bike_fleet_operation_platform.Services.GbfsHttp.GetJsonAsync(http, "station_information.json", 3, default);
    using var statusFeed = await Bike_fleet_operation_platform.Services.GbfsHttp.GetJsonAsync(http, "station_status.json", 3, default);
    var info = infoFeed.RootElement.GetProperty("data").GetProperty("stations").EnumerateArray()
        .Select(x => System.Text.Json.JsonSerializer.Deserialize<Bike_fleet_operation_platform.Dtos.StationInformationDto>(x, jsonOptions)!).ToDictionary(x => x.Station_id);
    var status = statusFeed.RootElement.GetProperty("data").GetProperty("stations").EnumerateArray()
        .Select(x => System.Text.Json.JsonSerializer.Deserialize<Bike_fleet_operation_platform.Dtos.StationStatusDto>(x, jsonOptions)!).ToList();
    Check("Live GBFS source contains station metadata and status", info.Count > 0 && status.Count > 0);
    var analyses = status.Where(x => info.ContainsKey(x.Station_id)).Select(x => engine.Analyze(new StationInput(x.Station_id,
        info[x.Station_id].Name, info[x.Station_id].Capacity, x.Num_bikes_available, x.Num_docs_available,
        x.Last_reported is > 0 ? DateTimeOffset.FromUnixTimeSeconds(x.Last_reported.Value).UtcDateTime : null,
        x.Is_renting == 1, x.Is_returning == 1, 0, 0, []), DateTime.UtcNow)).ToList();
    Check("Live bike feed has current healthy stations with real capacity", analyses.Any(x => !x.Stale && x.Status == "HEALTHY" && x.Station.Capacity > 0));
    Console.WriteLine($"Read-only Citi Bike check: {info.Count} stations; {analyses.Count(x => !x.Stale)} current, {analyses.Count(x => x.Status == "HEALTHY")} healthy, {analyses.Count(x => x.Status == "CRITICAL")} critical. No Kafka or database writes.");
}
Check("Empty-time metric excludes gaps and nonzero samples", MetricsService.EmptyMinutes([
    new(now.AddMinutes(-40), 0), new(now.AddMinutes(-35), 0), new(now.AddMinutes(-10), 0),
    new(now.AddMinutes(-5), 2), new(now, 0)], 10) == 5);
Check("Empty-time metric deduplicates source timestamps", MetricsService.EmptyMinutes([new(now.AddMinutes(-5), 0), new(now, 0), new(now, 0)], 10) == 5);
Check("No readings do not invent empty time", MetricsService.EmptyMinutes([], 10) == 0);
foreach (var (value, expected) in new (double, string)[] { (0,"0 minutes"), (1,"1 minute"), (45,"45 minutes"), (60,"1 hour"), (61,"1 hour 1 minute"), (120,"2 hours"), (306,"5 hours 6 minutes"), (.1,"1 minute"), (60.1,"1 hour 1 minute") })
    Check($"Duration {value} => {expected}", StationApi.Models.DurationText.Format(value) == expected);
foreach (var value in new double?[] { null, -1, double.NaN, double.PositiveInfinity })
    Check("Invalid duration is not displayed as a time", StationApi.Models.DurationText.Format(value) == "Not predicted");
Check("Single issue resolved restores healthy operation", StationStateService.Apply(healthy, 0, 0).OperationalStatus == "ACTIVE");
Check("Another open report keeps maintenance state", StationStateService.Apply(healthy, 0, 1).OperationalStatus == "MAINTENANCE");
Check("Another open alert keeps station attention", StationStateService.Apply(healthy, 1, 0).OperationalStatus == "ATTENTION");
Check("Resolution never overrides live critical availability", StationStateService.Apply(low, 0, 0).OperationalStatus == "ATTENTION");
Check("Stale telemetry cannot become active after resolution", StationStateService.Apply(stale, 0, 0).OperationalStatus == "UNKNOWN");
var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<StationApi.Models.FleetUser>();
var rider = new StationApi.Models.FleetUser { Name = "Rider", Role = "RIDER" };
var passwordHash = hasher.HashPassword(rider, "Test-only-Password-123!");
Check("Passwords are hashed and a valid password verifies", passwordHash != "Test-only-Password-123!" && hasher.VerifyHashedPassword(rider, passwordHash, "Test-only-Password-123!") != Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed);
Check("Incorrect password fails validation", hasher.VerifyHashedPassword(rider, passwordHash, "Wrong-password") == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed);
var invalidRegistration = new StationApi.Controllers.RegisterRequest(" ", "invalid", "short");
Check("Registration validates name email and password on the server", !System.ComponentModel.DataAnnotations.Validator.TryValidateObject(invalidRegistration,
    new System.ComponentModel.DataAnnotations.ValidationContext(invalidRegistration), new List<System.ComponentModel.DataAnnotations.ValidationResult>(), true));
var goodRegistration = new StationApi.Controllers.RegisterRequest("Rider", "rider@example.invalid", "Test-password-123!");
Check("Valid registration passes server validation", System.ComponentModel.DataAnnotations.Validator.TryValidateObject(goodRegistration,
    new System.ComponentModel.DataAnnotations.ValidationContext(goodRegistration), new List<System.ComponentModel.DataAnnotations.ValidationResult>(), true));
using var blankName = System.Text.Json.JsonDocument.Parse("{\"name\":\"   \",\"id\":\"123\"}");
Check("Station name fallback is never blank", StationApi.Models.ViewText.StationName(blankName.RootElement) == "Station #123");
Console.WriteLine($"{passed} business checks passed.");
await CacheChecks.Run();
await HttpChecks.Run();
await WebChecks.Run(args.Contains("--serve"));
