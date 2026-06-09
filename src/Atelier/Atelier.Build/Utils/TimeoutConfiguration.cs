namespace Atelier.Build.Utils;

public sealed class TimeoutConfiguration
{
        public TimeSpan DotnetBuild { get; init; } = TimeSpan.FromMinutes(10);

        public TimeSpan DotnetTest { get; init; } = TimeSpan.FromMinutes(15);

        public TimeSpan DockerBuild { get; init; } = TimeSpan.FromMinutes(30);

        public TimeSpan DockerCleanup { get; init; } = TimeSpan.FromSeconds(30);

        public TimeSpan CoverageReport { get; init; } = TimeSpan.FromMinutes(5);

        public TimeSpan Benchmarks { get; init; } = TimeSpan.FromMinutes(60);

        public TimeSpan ProjectGeneration { get; init; } = TimeSpan.FromMinutes(2);

        public static TimeoutConfiguration Default => new();

        public static TimeoutConfiguration NoTimeouts => new()
    {
        DotnetBuild = Timeout.InfiniteTimeSpan,
        DotnetTest = Timeout.InfiniteTimeSpan,
        DockerBuild = Timeout.InfiniteTimeSpan,
        DockerCleanup = Timeout.InfiniteTimeSpan,
        CoverageReport = Timeout.InfiniteTimeSpan,
        Benchmarks = Timeout.InfiniteTimeSpan,
        ProjectGeneration = Timeout.InfiniteTimeSpan
    };
}
