using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Atelier.Build.Pipeline;

public sealed class PlatformProbe
{
    public string GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macos";
        }
        return "unknown";
    }

    public bool IsToolAvailable(string toolName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = toolName,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
