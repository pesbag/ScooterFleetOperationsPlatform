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
Check("Outflow is six scooters per hour", low.RatePerHour == -6);
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
Check("Zero capacity is excluded from moves", zero.Stale && zero.Status == "WARNING");
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
Check("Closed station cannot supply scooters", engine.Recommend([low, closed], new HashSet<string>()).Count == 0);
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
Console.WriteLine($"{passed} business checks passed.");
await WebChecks.Run(args.Contains("--serve"));
