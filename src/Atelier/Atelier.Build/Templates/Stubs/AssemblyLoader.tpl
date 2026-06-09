namespace Atelier.Host.{{ boutiqueName }};

internal static class AssemblyLoader{{ boutiqueName }}
{
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;

        {{ assemblyLoads }}

        {{ typeTouches }}
    }

    private static void LoadAssemblyByName(string assemblyName)
    {
        try
        {
            System.Reflection.Assembly.Load(assemblyName);
        }
        catch
        {
        }
    }
}
