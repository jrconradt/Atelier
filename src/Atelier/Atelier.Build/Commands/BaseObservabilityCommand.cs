using System.CommandLine;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;

namespace Atelier.Build.Commands;

public abstract class BaseObservabilityCommand : Command
{
    protected BaseObservabilityCommand(string name, string description)
        : base(name, description)
    {
    }

        protected BuildContext CreateBuildContext(bool verbose = false)
    {
        return new BuildContext
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            Verbose = verbose
        };
    }

        protected BuildStateManager CreateStateManager(bool verbose = false)
    {
        var context = CreateBuildContext(verbose);
        var discoverer = new SubsystemDiscoverer(context);
        return new BuildStateManager(context, discoverer);
    }

        protected async Task<SubsystemDiscoverer> CreateDiscovererAsync(bool verbose = false)
    {
        var context = CreateBuildContext(verbose);
        return new SubsystemDiscoverer(context);
    }

        protected async Task<IReadOnlyList<SubsystemDefinition>> GetAllSubsystemsAsync(bool verbose = false)
    {
        var context = CreateBuildContext(verbose);
        var discoverer = new SubsystemDiscoverer(context);
        return await discoverer.DiscoverAsync().ConfigureAwait(false);
    }

        protected async Task<SubsystemDefinition?> GetSubsystemAsync(string name, bool verbose = false)
    {
        var context = CreateBuildContext(verbose);
        var discoverer = new SubsystemDiscoverer(context);
        return await discoverer.GetByNameAsync(name).ConfigureAwait(false);
    }

        protected bool IsPiped => Console.IsOutputRedirected;

        protected static bool NoColorRequested => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

        protected string ResolveFormat(string requestedFormat)
    {
        if (requestedFormat != "auto")
        {
            return requestedFormat;
        }
        if (IsPiped || NoColorRequested)
        {
            return "plain";
        }
        return "table";
    }

        protected void WriteLine(string message, bool respectPipeMode = true)
    {
        if (respectPipeMode && IsPiped)
        {
            Console.WriteLine(message);
        }
        else
        {
            Spectre.Console.AnsiConsole.WriteLine(message);
        }
    }

        protected Option<string> CreateFormatOption()
    {
        return new Option<string>("--format")
        {
            DefaultValueFactory = _ => "auto",
            Description = "Output format (auto, table, plain, json, csv)"
        };
    }
}
