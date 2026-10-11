namespace FleetShared;

// Query results expire after exactly 60 seconds. The revision is an invalidation token, not a query result.
public static class FleetCacheContract
{
    public const int TtlSeconds = 60;
    public const string RevisionKey = "bikefleet:stations:revision";
    public const string KeyPrefix = "bikefleet:query:v2:";
}
