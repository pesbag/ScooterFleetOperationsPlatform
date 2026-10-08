# Smart Fleet — explanation for the project defense

The engine is ordinary C# in `StationApi/Services/SmartFleetEngine.cs`. It has no database or UI dependencies. It receives readings and the current time, and returns an analysis. There is no machine learning.

## Data and units

`Count` is available scooters; `Capacity` is the total station capacity. Available return spaces are read separately because disabled docks may reduce real return capacity. The ingestion DTO keeps legacy names to avoid breaking existing Kafka messages. User-facing screens use scooter terminology.

Each status message produces an idempotent history record in MongoDB. Its identity is station ID plus source timestamp. Replaying the same Kafka message cannot create duplicate readings. The latest document never moves backward in source time. History expires after seven days; analysis uses the last two hours by default.

## Trend and predictions

1. Exclude invalid readings and readings outside the configured recent window.
2. Sort distinct source timestamps; require at least two readings spanning five minutes.
3. `net scooters per hour = (latest count - earliest count) / elapsed hours`.
4. If the trend is negative, `minutes until empty = current count / abs(rate) * 60`.
5. If positive, `minutes until full = (capacity - current count) / rate * 60`.

Example: 4 scooters now, 10 one hour ago: net rate is -6/hour. Estimated time to empty is 40 minutes.

This estimates **net change**, not separate gross arrivals and departures: snapshots cannot reveal simultaneous trips in both directions. It assumes the recent net rate continues. It does not know future weather, events or trip destinations.

Missing data, future timestamps or data older than ten minutes suppress predictions and new moves. No history means no prediction, rather than an invented zero trend. Out-of-range counts and zero capacity are flagged for verification.

## Explainable risk score

Take the maximum applicable score:

| Condition | Score |
|---|---:|
| Balanced station | 15 |
| Below 25% or above 75% utilization | 55 |
| At/below 10% or at/above 90% | 85 |
| Empty/full predicted within 120 minutes | 75 |
| Empty/full predicted within 30 minutes | 95 |
| Stale data or closed rental/return service | at least 70 |

Risk labels: 0–39 LOW, 40–69 MEDIUM, 70–84 HIGH, 85–100 CRITICAL.
Station statuses: below 40 HEALTHY, 40–84 WARNING, 85+ CRITICAL.
The stale flag is displayed separately; high inventory risk is not evidence of a trustworthy prediction.

`FleetOptions` centralizes thresholds. ASP.NET configuration can override them with `SmartFleet__LowRatio`, `SmartFleet__HighRatio`, `SmartFleet__TargetRatio`, `SmartFleet__WarningMinutes`, `SmartFleet__CriticalMinutes`, `SmartFleet__StaleMinutes`, `SmartFleet__HistoryMinutes`, `SmartFleet__MaxMove` and `SmartFleet__RecommendationCooldownMinutes`.

## Rebalancing

The target is 50% utilization. A source can provide the scooters above its target. A destination can receive scooters up to its target. Generate a move only when one side has a shortage or crowding problem, or a near-term prediction of one.

Quantity is the minimum of source excess, destination need, actual available return spaces, and the configured maximum (12). A source approaching shortage and a destination approaching full are excluded. Data must be fresh and rental/return service available. Candidates are ordered by operational risk, then approximate coordinate distance; this is not a route planner.

Each station can participate in only one active recommendation. Pending/in-progress tasks reserve both stations. Closed tasks reserve them for a short cooldown (15 minutes), preventing the same task from reappearing immediately. The engine can generate new tasks later if telemetry still shows an imbalance.

The analysis worker runs every 30 seconds. Infeasible pending tasks are dismissed. In-progress tasks remain visible for the operator to complete or dismiss. Starting a task checks availability again. Completing it records the operator's report and audit ID; it does not forge telemetry or pretend that the physical move occurred automatically.

## Alerts and persistence

One active alert is maintained per problematic station. Its severity and explanation update as conditions change. It resolves automatically when the station is healthy, or an operator can resolve it. A resolved persistent problem can reappear after the cooldown.

Users, alerts and recommendations are persisted with EF Core in a dedicated MySQL database, `fleetOperationsDb`. Existing station metadata stays in `stationDb`. Only one API instance should run: an in-process semaphore coordinates the background worker and operator writes. Multiple API replicas would require database-level coordination and are outside this student project's scope.

## What to explain to an examiner

Follow one reading: source → Kafka → processor → MongoDB/Redis → API analysis → Razor View. Then demonstrate an admin creating an operator, an operator denied user-management access, and a completed recommendation remaining completed after signing out and back in.

The key distinction: telemetry describes what the source reports; predictions estimate what could happen; recommendations propose physical work; completion records what an operator says they performed.
