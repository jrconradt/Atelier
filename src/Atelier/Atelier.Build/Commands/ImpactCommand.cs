using System.CommandLine;
using System.Text.Json;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Commands;

public class ImpactCommand : Command
{
    public ImpactCommand() : base("impact", "Analyze change impact for a subsystem")
    {

        var subsystemArg = new Argument<string>("subsystem")
        {
            Description = "Subsystem to analyze impact for"
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            DefaultValueFactory = _ => "auto",
            Description = "Output format (auto, plain, json)"
        };

        var buildOrderOption = new Option<bool>("--build-order", "-b")
        {
            Description = "Show required build order"
        };

        Add(subsystemArg);
        Add(formatOption);
        Add(buildOrderOption);

        this.SetAction(async parseResult =>
        {
            await ExecuteAsync(parseResult.GetValue(subsystemArg)!,
                               parseResult.GetValue(formatOption)!,
                               parseResult.GetValue(buildOrderOption)).ConfigureAwait(false);
        });
    }

    private async Task ExecuteAsync(
        string subsystemName,
        string format,
        bool showBuildOrder)
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

        var analyzer = new DependencyAnalyzer(subsystems);
        var directDependents = analyzer.GetDirectDependents(subsystemName);
        var transitiveDependents = analyzer.GetTransitiveDependents(subsystemName, maxDepth: null);
        var indirectDependents = transitiveDependents.Except(directDependents).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var effectiveFormat = format == "auto"
            ? (Console.IsOutputRedirected ? "plain" : "table")
            : format;

        if (effectiveFormat == "json")
        {
            OutputJson(subsystemName, directDependents, indirectDependents,
                showBuildOrder ? analyzer.GetBuildOrder([subsystemName, .. transitiveDependents]) : null);
            return;
        }

        if (effectiveFormat == "plain")
        {
            OutputPlain(subsystemName, directDependents, indirectDependents);
            return;
        }

        OutputInteractive(subsystemName, directDependents, indirectDependents,
            showBuildOrder, analyzer, transitiveDependents);
    }

    private void OutputInteractive(
        string subsystemName,
        HashSet<string> directDependents,
        HashSet<string> indirectDependents,
        bool showBuildOrder,
        DependencyAnalyzer analyzer,
        HashSet<string> transitiveDependents)
    {
        var totalImpact = directDependents.Count + indirectDependents.Count;

        var summary = new Panel(
            $"[cyan]{totalImpact}[/] subsystems will be affected\n" +
            $"[green]{directDependents.Count}[/] direct dependents\n" +
            $"[yellow]{indirectDependents.Count}[/] indirect dependents")
            .Border(BoxBorder.Rounded)
            .Header("[yellow]Summary[/]");

        AnsiConsole.Write(summary);
        AnsiConsole.WriteLine();

        if (directDependents.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold]Direct Dependents[/] (will require rebuild):");
            foreach (var dep in directDependents.OrderBy(d => d))
            {
                AnsiConsole.MarkupLine($"  • [green]{dep}[/]");
            }
            AnsiConsole.WriteLine();
        }

        if (indirectDependents.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold]Indirect Dependents[/] (may require rebuild):");
            foreach (var dep in indirectDependents.OrderBy(d => d))
            {
                AnsiConsole.MarkupLine($"  • [yellow]{dep}[/]");
            }
            AnsiConsole.WriteLine();
        }

        if (totalImpact == 0)
        {
            AnsiConsole.MarkupLine("[dim]No subsystems depend on {0}[/]", subsystemName);
            AnsiConsole.WriteLine();
        }

        if (showBuildOrder)
        {
            var buildOrder = analyzer.GetBuildOrder([subsystemName, .. transitiveDependents]);

            if (buildOrder.Count > 0)
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Order")
                    .AddColumn("Subsystem")
                    .AddColumn("Type");

                for (int i = 0; i < buildOrder.Count; i++)
                {
                    var sub = buildOrder[i];
                    var type = sub.Equals(subsystemName, StringComparison.OrdinalIgnoreCase) ? "[red]Changed[/]"
                        : directDependents.Contains(sub) ? "[green]Direct[/]"
                        : "[yellow]Indirect[/]";

                    table.AddRow((i + 1).ToString(), sub, type);
                }

                AnsiConsole.Write(new Rule("[blue]Suggested Build Order[/]").RuleStyle("dim"));
                AnsiConsole.WriteLine();
                AnsiConsole.Write(table);
            }
        }
    }

    private void OutputPlain(
        string subsystemName,
        HashSet<string> directDependents,
        HashSet<string> indirectDependents)
    {

        Console.WriteLine($"Subsystem: {subsystemName}");
        Console.WriteLine($"Direct: {directDependents.Count}");
        Console.WriteLine($"Indirect: {indirectDependents.Count}");
        Console.WriteLine();

        foreach (var dep in directDependents.OrderBy(d => d))
        {
            Console.WriteLine($"DIRECT\t{dep}");
        }

        foreach (var dep in indirectDependents.OrderBy(d => d))
        {
            Console.WriteLine($"INDIRECT\t{dep}");
        }
    }

    private void OutputJson(
        string subsystemName,
        HashSet<string> directDependents,
        HashSet<string> indirectDependents,
        IReadOnlyList<string>? buildOrder)
    {
        var data = new
        {
            subsystem = subsystemName,
            directDependents = directDependents.OrderBy(d => d).ToList(),
            indirectDependents = indirectDependents.OrderBy(d => d).ToList(),
            totalImpact = directDependents.Count + indirectDependents.Count,
            buildOrder = buildOrder?.ToList()
        };

        Console.WriteLine(JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
