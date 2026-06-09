using System.CommandLine;
using Atelier.Build.Discovery;
using Atelier.Build.Formatters;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Commands;

public enum TreeFormat { Auto, Ascii, Mermaid, Json, Plain }

public class TreeCommand : Command
{
    private static TreeFormat ParseFormat(string raw) => raw switch
    {
        "auto" => TreeFormat.Auto,
        "ascii" => TreeFormat.Ascii,
        "mermaid" => TreeFormat.Mermaid,
        "json" => TreeFormat.Json,
        "plain" => TreeFormat.Plain,
        _ => throw new ArgumentException($"Unknown format: {raw}")
    };

    public TreeCommand() : base("tree", "Visualize subsystem dependency tree")
    {

        var subsystemArg = new Argument<string?>("subsystem")
        {
            DefaultValueFactory = _ => null,
            Description = "Subsystem to focus on (shows all if omitted)"
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            DefaultValueFactory = _ => "auto",
            Description = "Output format (auto, ascii, mermaid, json, plain)"
        };

        var directionOption = new Option<string>("--direction", "-d")
        {
            DefaultValueFactory = _ => "dependencies",
            Description = "Direction (dependencies, dependents, both)"
        };

        var depthOption = new Option<int?>("--depth")
        {
            DefaultValueFactory = _ => null,
            Description = "Limit dependency depth"
        };

        var impactsOption = new Option<bool>("--impacts", "-i")
        {
            Description = "Highlight subsystems affected by changes"
        };

        Add(subsystemArg);
        Add(formatOption);
        Add(directionOption);
        Add(depthOption);
        Add(impactsOption);

        this.SetAction(async parseResult =>
        {
            await ExecuteAsync(parseResult.GetValue(subsystemArg),
                               parseResult.GetValue(formatOption)!,
                               parseResult.GetValue(directionOption)!,
                               parseResult.GetValue(depthOption),
                               parseResult.GetValue(impactsOption)).ConfigureAwait(false);
        });
    }

    private async Task ExecuteAsync(
        string? subsystemName,
        string format,
        string direction,
        int? depth,
        bool showImpacts)
    {

        var context = new BuildContext { WorkingDirectory = Directory.GetCurrentDirectory() };
        var discoverer = new SubsystemDiscoverer(context);
        var subsystems = await discoverer.DiscoverAsync().ConfigureAwait(false);

        if (subsystems.Count == 0)
        {
            if (!Console.IsOutputRedirected)
            {
                AnsiConsole.MarkupLine("[yellow]No subsystems found[/]");
            }
            return;
        }

        if (!string.IsNullOrEmpty(subsystemName))
        {
            var target = subsystems.FirstOrDefault(s =>
                s.Name.Equals(subsystemName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                if (!Console.IsOutputRedirected)
                {
                    AnsiConsole.MarkupLine($"[red]✗ Subsystem '{subsystemName}' not found[/]");
                }
                return;
            }
        }

        var analyzer = new DependencyAnalyzer(subsystems);

        HashSet<string>? impactSet = null;
        if (showImpacts && !string.IsNullOrEmpty(subsystemName))
        {
            impactSet = analyzer.GetImpactSet(subsystemName);
        }

        DependencyTreeNode tree;
        if (string.IsNullOrEmpty(subsystemName))
        {
            tree = BuildFullTree(analyzer, subsystems);
        }
        else
        {
            try
            {
                tree = analyzer.BuildTree(subsystemName, direction, depth, impactSet);
            }
            catch (ArgumentException ex)
            {
                if (!Console.IsOutputRedirected)
                {
                    AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                }
                return;
            }
        }

        TreeFormat requested;
        try
        {
            requested = ParseFormat(format);
        }
        catch (ArgumentException ex)
        {
            if (!Console.IsOutputRedirected)
            {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
            }
            return;
        }

        var effective = requested == TreeFormat.Auto
            ? (Console.IsOutputRedirected ? TreeFormat.Plain : TreeFormat.Ascii)
            : requested;

        var output = effective switch
        {
            TreeFormat.Ascii => DependencyTreeFormatter.FormatAsAsciiTree(tree, impactSet),
            TreeFormat.Mermaid => DependencyTreeFormatter.FormatAsMermaid(tree, impactSet),
            TreeFormat.Json => DependencyTreeFormatter.FormatAsJson(tree),
            TreeFormat.Plain => DependencyTreeFormatter.FormatAsPlainText(tree),
            TreeFormat.Auto => throw new InvalidOperationException("Auto must resolve before render"),
            _ => throw new System.Diagnostics.UnreachableException(),
        };

        Console.WriteLine(output);

        if (showImpacts && impactSet?.Count > 0 && !Console.IsOutputRedirected)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]Impact: {impactSet.Count} subsystems affected (including {subsystemName})[/]");
        }
    }

    private DependencyTreeNode BuildFullTree(
        DependencyAnalyzer analyzer,
        IReadOnlyList<SubsystemDefinition> subsystems)
    {

        var root = new DependencyTreeNode
        {
            Name = "all-subsystems",
            Children = new()
        };

        foreach (var subsystem in subsystems.OrderBy(s => s.Name))
        {
            var deps = analyzer.GetDirectDependencies(subsystem.Name);
            var dependents = analyzer.GetDirectDependents(subsystem.Name);

            root.Children.Add(new DependencyTreeNode
            {
                Name = subsystem.Name,
                DependencyCount = deps.Count,
                DependentCount = dependents.Count
            });
        }

        return root;
    }
}
