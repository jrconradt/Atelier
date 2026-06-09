using System.CommandLine;
using Atelier.Build.Commands;
using Microsoft.Build.Locator;
using Spectre.Console;
using Vice.Host;
using Vice.Options;

if (!MSBuildLocator.IsRegistered)
{
    MSBuildLocator.RegisterDefaults();
}

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
{
    AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors
    });
}

var pathResolution = ResolvePathOption(args);
if (pathResolution.Error != null)
{
    AnsiConsole.MarkupLine($"[red]{Markup.Escape(pathResolution.Error)}[/]");
    return 1;
}

if (pathResolution.Path != null)
{
    Directory.SetCurrentDirectory(pathResolution.Path);
}

args = pathResolution.RemainingArgs;

var viceVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "smash", "allsmash", "test", "kill"
};

bool useVice;
string[] viceArgs;

if (args.Length == 0)
{
    useVice = true;
    viceArgs = ["smash"];
}
else if (viceVerbs.Contains(args[0]))
{
    useVice = true;
    viceArgs = args;
}
else if (CommandRegistry.IsCommand(args[0]))
{
    useVice = false;
    viceArgs = args;
}
else
{

    useVice = true;
    viceArgs = ["smash", .. args];
}

if (useVice)
{
    await using var app = ViceApp.Create("smash", "0.5.3")
        .WithDescription("The Atelier build system")
        .WithGlobalOption(
            new FlagOption("diagram", "Generate a mermaid architecture diagram"),
            new FlagOption("generate-boutiques", "Generate boutique projects"),
            new FlagOption("test", "Run the generated test suite after building"),
            new FlagOption("benchmark", "Run benchmarks after building"),
            new FlagOption("allow-benchmark-regression", "Do not fail the build on benchmark regressions"),
            new FlagOption("no-incremental", "Disable incremental build"),
            new FlagOption("no-coverage", "Disable code coverage collection"),
            new FlagOption("skip-docker", "Skip Docker steps in the full pipeline"),
            new FlagOption("force", "Skip confirmation prompts"),
            new ValueBearingOption("max-nf", "Maximum allowed non-functional test failures"),
            new ValueBearingOption("nf-allowlist", "Path to the non-functional failure allowlist"),
            new ValueBearingOption("pattern", "Filter target processes by command-line pattern"),
            new ValueBearingOption("path", "Run smash from this directory / solution root (also -C)"))
        .Build();
    ViceCommands.Register(app);
    return await app.RunAsync(viceArgs).ConfigureAwait(false);
}

var rootCommand = new RootCommand("smash - The Atelier build system");
foreach (var command in CommandRegistry.DiscoverCommands())
{
    rootCommand.Add(command);
}

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);

static (string? Path, string? Error, string[] RemainingArgs) ResolvePathOption(string[] arguments)
{
    var remaining = new List<string>();
    string? requested = null;

    for (var i = 0; i < arguments.Length; i++)
    {
        var arg = arguments[i];

        if (arg == "-C" || arg == "--path")
        {
            if (i + 1 >= arguments.Length)
            {
                return (null, $"Option '{arg}' requires a directory path", arguments);
            }

            requested = arguments[i + 1];
            i++;
            continue;
        }

        if (arg.StartsWith("-C=", StringComparison.Ordinal))
        {
            requested = arg["-C=".Length..];
            continue;
        }

        if (arg.StartsWith("--path=", StringComparison.Ordinal))
        {
            requested = arg["--path=".Length..];
            continue;
        }

        remaining.Add(arg);
    }

    if (requested == null)
    {
        return (null, null, arguments);
    }

    if (requested.Length == 0)
    {
        return (null, "Option '--path' requires a non-empty directory path", arguments);
    }

    var resolved = Path.GetFullPath(requested);
    if (!Directory.Exists(resolved))
    {
        return (null, $"Path not found: {resolved}", arguments);
    }

    return (resolved, null, remaining.ToArray());
}
