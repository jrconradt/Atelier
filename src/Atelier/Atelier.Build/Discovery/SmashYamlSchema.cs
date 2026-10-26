using YamlDotNet.Serialization;

namespace Atelier.Build.Discovery;

public class SmashYamlSchema
{
        [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

        [YamlMember(Alias = "description")]
    public string? Description { get; set; }

        [YamlMember(Alias = "solution")]
    public string Solution { get; set; } = string.Empty;

        [YamlMember(Alias = "dependencies")]
    public List<string> Dependencies { get; set; } = [];

        [YamlMember(Alias = "test")]
    public SmashTestConfig? Test { get; set; }

        [YamlMember(Alias = "benchmark")]
    public SmashBenchmarkConfig? Benchmark { get; set; }

        [YamlMember(Alias = "build")]
    public SmashBuildConfig? Build { get; set; }

        [YamlMember(Alias = "pre_build")]
    public PreBuildConfig? PreBuild { get; set; }

        [YamlMember(Alias = "post_build")]
    public PreBuildConfig? PostBuild { get; set; }

        [YamlMember(Alias = "post_test")]
    public PreBuildConfig? PostTest { get; set; }

        public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("'name' is required");
        }

        if (string.IsNullOrWhiteSpace(Solution))
        {
            errors.Add("'solution' is required");
        }

        if (Build is not null
            && Build.Configuration != "Debug"
            && Build.Configuration != "Release")
        {
            errors.Add($"build.configuration '{Build.Configuration}' is invalid; expected 'Debug' or 'Release'");
        }

        if (Test?.Coverage is not null
            && (Test.Coverage.Threshold < 0 || Test.Coverage.Threshold > 100))
        {
            errors.Add($"test.coverage.threshold {Test.Coverage.Threshold} is out of range 0-100");
        }

        return errors;
    }
}

public class SmashTestConfig
{
    [YamlMember(Alias = "projects")]
    public List<string> Projects { get; set; } = [];

        [YamlMember(Alias = "output")]
    public TestOutputConfig? Output { get; set; }

        [YamlMember(Alias = "coverage")]
    public CoverageConfig? Coverage { get; set; }
}

public class TestOutputConfig
{
        [YamlMember(Alias = "loggers")]
    public List<string>? Loggers { get; set; }

        [YamlMember(Alias = "directory")]
    public string? Directory { get; set; }
}

public class CoverageConfig
{
        [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

        [YamlMember(Alias = "threshold")]
    public int Threshold { get; set; } = 80;

        [YamlMember(Alias = "formats")]
    public List<string> Formats { get; set; } = ["cobertura", "opencover", "json"];

        [YamlMember(Alias = "html_report")]
    public bool HtmlReport { get; set; } = true;

        [YamlMember(Alias = "exclude")]
    public List<string> Exclude { get; set; } = ["*.Tests", "*.Benchmarks"];

        [YamlMember(Alias = "include")]
    public List<string>? Include { get; set; }
}

public class SmashBenchmarkConfig
{
    [YamlMember(Alias = "project")]
    public string? Project { get; set; }

        [YamlMember(Alias = "output")]
    public BenchmarkOutputConfig? Output { get; set; }
}

public class BenchmarkOutputConfig
{
        [YamlMember(Alias = "directory")]
    public string? Directory { get; set; }

        [YamlMember(Alias = "exporters")]
    public List<string>? Exporters { get; set; }
}

public class SmashBuildConfig
{
    [YamlMember(Alias = "configuration")]
    public string Configuration { get; set; } = "Debug";

    [YamlMember(Alias = "parallel")]
    public bool Parallel { get; set; } = true;

    [YamlMember(Alias = "target_framework")]
    public string TargetFramework { get; set; } = "net10.0";

    [YamlMember(Alias = "sdk_image_digest")]
    public string? SdkImageDigest { get; set; }

    [YamlMember(Alias = "runtime_image_digest")]
    public string? RuntimeImageDigest { get; set; }
}

public class PreBuildConfig
{
    [YamlMember(Alias = "linux")]
    public List<PreBuildStep>? Linux { get; set; }

    [YamlMember(Alias = "windows")]
    public List<PreBuildStep>? Windows { get; set; }

    [YamlMember(Alias = "macos")]
    public List<PreBuildStep>? MacOS { get; set; }
}

public class PreBuildStep
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "command")]
    public string Command { get; set; } = string.Empty;

    [YamlMember(Alias = "working_directory")]
    public string? WorkingDirectory { get; set; }

    [YamlMember(Alias = "required_tools")]
    public List<string>? RequiredTools { get; set; }

    [YamlMember(Alias = "skip_if_missing")]
    public bool SkipIfMissing { get; set; } = false;

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }
}
