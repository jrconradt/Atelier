using System.Runtime.InteropServices;

namespace Atelier.Build.MetaOptimization;

public sealed record CpuInfo
{
    public required string Vendor { get; init; }
    public required string ModelName { get; init; }
    public required int LogicalCores { get; init; }

        public static CpuInfo Detect()
    {
        var vendor = DetectVendor();
        var modelName = DetectModelName();
        var logicalCores = Environment.ProcessorCount;

        return new CpuInfo
        {
            Vendor = vendor,
            ModelName = modelName,
            LogicalCores = logicalCores
        };
    }

    private static string DetectVendor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return DetectVendorWindows();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return DetectVendorLinux();
        }
        else
        {
            return "Unknown";
        }
    }

    private static string DetectVendorWindows()
    {
        try
        {

            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wmic",
                Arguments = "cpu get manufacturer /value",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
            {
                return "Unknown";
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var manufacturerLine = output.Split('\n')
                .FirstOrDefault(line => line.StartsWith("Manufacturer="));

            if (manufacturerLine == null)
            {
                return "Unknown";
            }

            var manufacturer = manufacturerLine.Split('=')[1].Trim();

            return manufacturer switch
            {
                "GenuineIntel" => "Intel",
                "AuthenticAMD" => "AMD",
                _ => manufacturer
            };
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string DetectVendorLinux()
    {
        try
        {
            var cpuinfo = File.ReadAllLines("/proc/cpuinfo");
            var vendorLine = cpuinfo.FirstOrDefault(line => line.StartsWith("vendor_id"));

            if (vendorLine == null)
            {
                return "Unknown";
            }

            var vendor = vendorLine.Split(':')[1].Trim();

            return vendor switch
            {
                "GenuineIntel" => "Intel",
                "AuthenticAMD" => "AMD",
                _ => vendor
            };
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string DetectModelName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return DetectModelNameWindows();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return DetectModelNameLinux();
        }
        else
        {
            return "Unknown";
        }
    }

    private static string DetectModelNameWindows()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wmic",
                Arguments = "cpu get name /value",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
            {
                return "Unknown";
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var nameLine = output.Split('\n')
                .FirstOrDefault(line => line.StartsWith("Name="));

            if (nameLine == null)
            {
                return "Unknown";
            }

            return nameLine.Split('=')[1].Trim();
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string DetectModelNameLinux()
    {
        try
        {
            var cpuinfo = File.ReadAllLines("/proc/cpuinfo");
            var modelLine = cpuinfo.FirstOrDefault(line => line.StartsWith("model name"));

            if (modelLine == null)
            {
                return "Unknown";
            }

            return modelLine.Split(':')[1].Trim();
        }
        catch
        {
            return "Unknown";
        }
    }
}
