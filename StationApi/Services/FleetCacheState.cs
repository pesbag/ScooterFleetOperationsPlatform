namespace StationApi.Services;

// One API process shares a fill gate and a local generation for immediate mutation visibility.
public sealed class FleetCacheState
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public string Generation { get; set; } = Guid.NewGuid().ToString("N");
}
