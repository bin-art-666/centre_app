namespace centre_app;

internal sealed record PerformanceProfile(
    int IconLoadConcurrency,
    bool UseLowLatencyGc,
    int PageAnimationDurationMs,
    double PageSlideDistance)
{
    public static PerformanceProfile Current { get; } = Create(
        Environment.ProcessorCount,
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

    internal static PerformanceProfile Create(int logicalProcessors, long availableMemoryBytes)
    {
        const long gibibyte = 1024L * 1024 * 1024;
        // The GC normally reports a usable budget below installed physical RAM.
        // A 32 GB machine commonly exposes roughly 23–24 GiB here.
        if (logicalProcessors >= 16 && availableMemoryBytes >= 20 * gibibyte)
            return new PerformanceProfile(6, true, 150, 30);
        if (logicalProcessors >= 8 && availableMemoryBytes >= 10 * gibibyte)
            return new PerformanceProfile(4, true, 155, 34);
        return new PerformanceProfile(2, false, 165, 28);
    }
}
