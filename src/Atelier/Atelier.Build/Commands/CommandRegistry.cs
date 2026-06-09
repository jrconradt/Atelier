using System.CommandLine;

namespace Atelier.Build.Commands;

public static class CommandRegistry
{
        public static IEnumerable<Command> DiscoverCommands()
    {
        return
        [
            new AnalyzeCommand(),
            new ArtifactsCommand(),
            new BaselineCommand(),
            new DashboardCommand(),
            new DockerCommand(),
            new HistoryCommand(),
            new ImpactCommand(),
            new StatusCommand(),
            new TreeCommand(),
            new TrendsCommand(),
            new UnsmashCommand(),
            new WatchCommand()
        ];
    }

        public static HashSet<string> GetCommandNames()
    {
        var commands = DiscoverCommands();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            names.Add(command.Name);
            foreach (var alias in command.Aliases)
            {
                names.Add(alias);
            }
        }

        names.Add("--help");
        names.Add("-h");
        names.Add("--version");
        names.Add("-?");

        return names;
    }

        public static bool IsCommand(string arg)
    {
        var commandNames = GetCommandNames();
        return commandNames.Contains(arg);
    }
}
