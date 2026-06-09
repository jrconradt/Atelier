namespace Atelier.Build.MetaOptimization;

public sealed class PlatformInfo
{
        public required string Cpu { get; init; }

        public required List<string> CpuFeatures { get; init; }

        public required int NormalizedFrequency { get; init; }

        public required string Os { get; init; }

        public required string Dotnet { get; init; }

        public static PlatformInfo Detect()
    {
        var cpu = DetectCpuModel();
        var cpuFeatures = DetectCpuFeatures();
        var frequency = DetectCpuFrequency();
        var os = DetectOs();
        var dotnet = DetectDotnetVersion();

        return new PlatformInfo
        {
            Cpu = cpu,
            CpuFeatures = cpuFeatures,
            NormalizedFrequency = frequency,
            Os = os,
            Dotnet = dotnet
        };
    }

    private static string DetectCpuModel()
    {
        if (OperatingSystem.IsWindows())
        {

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                var name = key?.GetValue("ProcessorNameString") as string;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
            }
        }
        else if (OperatingSystem.IsLinux())
        {

            try
            {
                var cpuinfo = File.ReadAllLines("/proc/cpuinfo");
                var modelLine = cpuinfo.FirstOrDefault(l => l.StartsWith("model name"));
                if (modelLine != null)
                {
                    var parts = modelLine.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        return parts[1].Trim();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown CPU";
    }

    private static List<string> DetectCpuFeatures()
    {
        var features = new List<string>();

        if (System.Runtime.Intrinsics.X86.Avx512F.IsSupported)
        {
            features.Add("AVX-512F");
        }
        if (System.Runtime.Intrinsics.X86.Avx512DQ.IsSupported)
        {
            features.Add("AVX-512DQ");
        }
        if (System.Runtime.Intrinsics.X86.Avx512BW.IsSupported)
        {
            features.Add("AVX-512BW");
        }
        if (System.Runtime.Intrinsics.X86.Avx512Vbmi.IsSupported)
        {
            features.Add("AVX-512VBMI");
        }
        if (System.Runtime.Intrinsics.X86.Gfni.IsSupported)
        {
            features.Add("GFNI");
        }
        if (System.Runtime.Intrinsics.X86.Popcnt.IsSupported)
        {
            features.Add("POPCNT");
        }
        if (System.Runtime.Intrinsics.X86.Lzcnt.IsSupported)
        {
            features.Add("LZCNT");
        }

        return features;
    }

    private static int DetectCpuFrequency()
    {


        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                var mhz = key?.GetValue("~MHz") as int?;
                if (mhz.HasValue && mhz.Value > 0)
                {
                    return mhz.Value;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                var cpuinfo = File.ReadAllLines("/proc/cpuinfo");
                var mhzLine = cpuinfo.FirstOrDefault(l => l.StartsWith("cpu MHz"));
                if (mhzLine != null)
                {
                    var parts = mhzLine.Split(':', 2);
                    if (parts.Length == 2 && double.TryParse(parts[1].Trim(), out var mhz))
                    {
                        return (int)mhz;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return 3600;
    }

    private static string DetectOs()
    {
        if (OperatingSystem.IsWindows())
        {

            var version = Environment.OSVersion.Version;
            if (version.Major == 10 && version.Build >= 22000)
            {
                return "Windows 11";
            }
            else if (version.Major == 10)
            {
                return "Windows 10";
            }
            return $"Windows {version.Major}.{version.Minor}";
        }
        else if (OperatingSystem.IsLinux())
        {

            try
            {
                var osRelease = File.ReadAllLines("/etc/os-release");
                var prettyName = osRelease
                    .FirstOrDefault(l => l.StartsWith("PRETTY_NAME="))
                    ?.Split('=', 2)[1]
                    .Trim('"');
                if (!string.IsNullOrWhiteSpace(prettyName))
                {
                    return prettyName;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            return "Linux";
        }
        else if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        return Environment.OSVersion.ToString();
    }

    private static string DetectDotnetVersion()
    {
        var version = Environment.Version;
        return $".NET {version.Major}.{version.Minor}";
    }

        public bool IsCompatibleWith(PlatformInfo other)
    {

        var ourFeatures = new HashSet<string>(CpuFeatures);
        var theirFeatures = new HashSet<string>(other.CpuFeatures);
        if (!ourFeatures.SetEquals(theirFeatures))
        {
            return false;
        }

        if (Dotnet.Split('.')[0] != other.Dotnet.Split('.')[0])
        {
            return false;
        }

        var ourOsFamily = Os.Split(' ')[0];
        var theirOsFamily = other.Os.Split(' ')[0];
        if (ourOsFamily != theirOsFamily)
        {
            return false;
        }

        return true;
    }
}
