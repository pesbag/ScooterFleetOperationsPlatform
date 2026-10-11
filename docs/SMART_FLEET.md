# Smart Fleet — explanation for the project defense

The engine is ordinary C# in `StationApi/Services/SmartFleetEngine.cs`. It has no database or UI dependencies. It receives readings and the current time, and returns an analysis. There is no machine learning.

## Data and units

`Count` is available bikes; `Capacity` is the total station capacity. Available return spaces are read separately because disabled docks may reduce real return capacity. The ingestion DTO keeps legacy names to avoid breaking existing Kafka messages. User-facing screens use bike terminology.

Each status message with a nonnegative count and valid source timestamp produces an idempotent history record in MongoDB. Its identity is station ID plus source timestamp. Replaying the same Kafka message cannot create duplicate readings. Valid latest readings do not move backward in source time. Missing or invalid timestamps are retained as a data-quality warning; a subsequent valid report can recover from an invalid future timestamp. History expires after seven days; analysis uses the last two hours by default.

## Missing, empty and outdated readings

Counts and service flags are nullable: a missing field stays unknown; an explicit zero remains zero. `ReceivedAt` is stamped by the ingestion service after receiving the source response. `ReportedAt` comes from the source station timestamp. Receiving an old report now does not make it current. Older stored documents without a receipt timestamp show a dash; receipt times are not invented retroactively.

Analysis returns `DataState`: CURRENT, MISSING, OUTDATED or INVALID. Missing required fields, impossible capacity/count values, reports older than ten minutes, or clocks over two minutes into the future suppress forecasts and moves. Such stations have a data-quality warning score of 70, not an inferred empty/full inventory alarm. The existing `Stale` flag also covers missing and invalid data so existing operation guards keep working.

The Razor table and station detail show both timestamps. Unverified availability is shown as Unknown, with the last reported count separately on the detail page. The dashboard's `availableBikes` replaces the misleading `totalBikes` field. It sums only stations with current valid data, shows how many stations are covered, and returns null when none qualify. This is not total fleet size: vehicles in use or maintenance may not be included. Page generation time is labelled separately from telemetry timestamps.

Legacy data that already stored a missing field as zero cannot be reliably repaired without new source readings. No historical data is rewritten by this change. The configured live source is Citi Bike GBFS 2.3. Production synthetic ingestion has been removed.

## Trend and predictions

1. Exclude invalid readings and readings outside the configured recent window.
2. Sort distinct source timestamps; require at least two readings spanning five minutes.
3. `net bikes per hour = (latest count - earliest count) / elapsed hours`.
4. If the trend is negative, `minutes until empty = current count / abs(rate) * 60`.
5. If positive, `minutes until full = (capacity - current count) / rate * 60`.

Example: 4 bikes now, 10 one hour ago: net rate is -6/hour. Estimated time to empty is 40 minutes.

This estimates **net change**, not separate gross arrivals and departures: snapshots cannot reveal simultaneous trips in both directions. It assumes the recent net rate continues. It does not know future weather, events or trip destinations.

Missing data, future timestamps or data older than ten minutes suppress predictions and new moves. No history means no prediction, rather than an invented zero trend. Out-of-range counts are flagged for verification. Capacity 0 is the existing storage sentinel for an unpublished capacity: availability and shortage trends remain usable, but utilization, full forecasts and transfer recommendations are withheld. It is never inferred by adding availability and return spaces.

## Explainable risk score

Take the maximum applicable score:

| Condition | Score |
|---|---:|
| Balanced station | 15 |
| Below 25% or above 75% utilization | 55 |
| At/below 10% or at/above 90% | 85 |
| Empty/full predicted within 120 minutes | 75 |
| Empty/full predicted within 30 minutes | 95 |
| Missing, invalid or outdated data | 70 (data-quality warning) |
| Closed rental/return service with valid data | at least 70 |

Risk labels: 0–39 LOW, 40–69 MEDIUM, 70–84 HIGH, 85–100 CRITICAL.
Station statuses: below 40 HEALTHY, 40–84 WARNING, 85+ CRITICAL.
The stale flag is displayed separately; high inventory risk is not evidence of a trustworthy prediction.

`FleetOptions` centralizes thresholds. ASP.NET configuration can override them with `SmartFleet__LowRatio`, `SmartFleet__HighRatio`, `SmartFleet__TargetRatio`, `SmartFleet__WarningMinutes`, `SmartFleet__CriticalMinutes`, `SmartFleet__StaleMinutes`, `SmartFleet__HistoryMinutes`, `SmartFleet__MaxMove` and `SmartFleet__RecommendationCooldownMinutes`.

## Rebalancing

The target is 50% utilization. A source can provide the bikes above its target. A destination can receive bikes up to its target. Generate a move only when one side has a shortage or crowding problem, or a near-term prediction of one.

Quantity is the minimum of source excess, destination need, actual available return spaces, and the configured maximum (12). A source approaching shortage and a destination approaching full are excluded. Data must be fresh and rental/return service available. Candidates are ordered by operational risk, then approximate coordinate distance; this is not a route planner.

Each station can participate in only one active recommendation. Pending/in-progress tasks reserve both stations. Closed tasks reserve them for a short cooldown (15 minutes), preventing the same task from reappearing immediately. The engine can generate new tasks later if telemetry still shows an imbalance.

The analysis worker runs every 30 seconds. Infeasible pending tasks are dismissed. In-progress tasks remain visible for the operator to complete or dismiss. Starting a task checks availability again. Completing it records the operator's report and audit ID; it does not forge telemetry or pretend that the physical move occurred automatically.

## Alerts and persistence

One active alert is maintained per problematic station. Its severity and explanation update as conditions change. It resolves automatically when the station is healthy, or an operator can resolve it. A resolved persistent problem can reappear after the cooldown.

Users, alerts and recommendations are persisted with EF Core in a dedicated MySQL database, `fleetOperationsDb`. Existing station metadata stays in `stationDb`. Only one API instance should run: an in-process semaphore coordinates the background worker and operator writes. Multiple API replicas would require database-level coordination and are outside this student project's scope.

## What to explain to an examiner

Follow one reading: source → Kafka → processor → MongoDB/Redis → API analysis → Razor View. Then demonstrate an admin creating an operator, an operator denied user-management access, and a completed recommendation remaining completed after signing out and back in.

The key distinction: telemetry describes what the source reports; predictions estimate what could happen; recommendations propose physical work; completion records what an operator says they performed.

## Query scope and cache

Single-station reads filter SQL metadata by station ID before fetching MongoDB status and history. List and station reads use cache-aside Redis results with exactly 60 seconds TTL, centralized in Shared/FleetCacheContract.cs. A shared fill gate reduces duplicate requests in this single-API deployment. Re-evaluation on cache hits uses source timestamps, not cache write time. Operational state is enriched from uncached issue records. Telemetry and metadata writes update a shared revision key; operator mutations also change the local generation so Redis failure cannot keep a known old local result in use. In-flight fills under an old revision cannot be read under the new one. Empty initial results and database errors are not cached.

Live source: Citi Bike, https://citibikenyc.com/system-data. Discovery: https://gbfs.citibikenyc.com/gbfs/2.3/gbfs.json. Station metadata supplies real capacity where available. Internal IDs are prefixed with citibike:; external field names stay unchanged. Old source records are preserved but excluded by this prefix.

HTML meta refresh updates read-only pages at the configured interval (60 seconds by default), with an explicit pause option. It is not used on registration/report/edit forms. This provides polling-based updates, not instantaneous streaming.

## Riders and measured outcomes

Public registration fixes the role to RIDER on the server. Rider API endpoints scope favorites and issue reports to the authenticated user ID. Operational API controllers explicitly require ADMIN or OPERATOR. Razor forms continue to use antiforgery validation. Operators can resolve rider reports with an audit timestamp and user ID.

The additive OperationsSchema upgrade creates four new tables in the existing operational database; it does not alter or delete the original tables. Empty time counts pairs of zero observations with gaps no larger than the freshness threshold, rather than extrapolating across missing telemetry. Task start/completion timestamps are recorded from now onward, not reconstructed from historical UpdatedAt values. Completion rate uses tasks created over seven days. Forecast samples preserve a 30-minute quantity prediction and match a subsequent reading in a five-minute tolerance window. Mean absolute error and its sample count are reported. Post-task destination count change compares task start with a reading 15–20 minutes after completion; it is observational and not a causal estimate.
