using System.Text.Json;
using Atelier.Build.Discovery;

namespace Atelier.Build.Pipeline;

public class BuildStateManager
{
    private readonly BuildContext _context;
    private readonly SubsystemDiscoverer _subsystemDiscoverer;
    private readonly string _stateFilePath;
    private readonly BuildState _state;

    public BuildStateManager(BuildContext context, SubsystemDiscoverer subsystemDiscoverer)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _subsystemDiscoverer = subsystemDiscoverer ?? throw new ArgumentNullException(nameof(subsystemDiscoverer));
        _stateFilePath = Path.Combine(_context.BuildOutputDirectory, "build-state.json");
        _state = LoadState();
    }

        public bool IsSubsystemStale(SubsystemDefinition subsystem)
    {

        if (!_state.Subsystems.TryGetValue(subsystem.Name, out var buildState))
        {
            return true;
        }

        if (!buildState.BuildSucceeded)
        {
            return true;
        }

        if (buildState.Configuration != subsystem.BuildConfiguration)
        {
            return true;
        }

        if (!DependenciesMatch(buildState.Dependencies, subsystem.Dependencies))
        {
            return true;
        }

        var currentSourceFiles = GetSourceFiles(subsystem);

        if (currentSourceFiles.Count != buildState.SourceFileTicks.Count)
        {
            return true;
        }

        foreach (var (filePath, currentTicks) in currentSourceFiles)
        {
            if (!buildState.SourceFileTicks.TryGetValue(filePath, out var recordedTicks) ||
                currentTicks > recordedTicks)
            {
                return true;
            }
        }

        if (currentSourceFiles.Values.Any(ticks => ticks > buildState.LastBuildTime.Ticks))
        {
            return true;
        }

        return false;
    }

        public void RecordBuild(SubsystemDefinition subsystem, bool succeeded)
    {
        var buildState = GetOrCreateSubsystemState(subsystem.Name);

        buildState.Name = subsystem.Name;
        buildState.LastBuildTime = DateTime.UtcNow;
        buildState.SourceFileTicks = GetSourceFiles(subsystem);
        buildState.OutputFileTicks = GetOutputFiles(subsystem);
        buildState.Dependencies = subsystem.Dependencies.ToList();
        buildState.BuildSucceeded = succeeded;
        buildState.Configuration = subsystem.BuildConfiguration;

        _state.Subsystems[subsystem.Name] = buildState;
    }

        public void RecordBuildTelemetry(
        string subsystemName,
        double buildDuration,
        TestResults? testResults = null,
        CoverageMetrics? coverage = null)
    {
        var buildState = GetOrCreateSubsystemState(subsystemName);

        buildState.BuildDuration = buildDuration;
        buildState.TestResults = testResults;
        buildState.Coverage = coverage;

        AddToHistory(buildState);

        _state.Subsystems[subsystemName] = buildState;
    }

        private void AddToHistory(SubsystemBuildState buildState)
    {
        var historyEntry = new BuildHistoryEntry
        {
            Timestamp = buildState.LastBuildTime,
            Duration = buildState.BuildDuration,
            Succeeded = buildState.BuildSucceeded,
            Configuration = buildState.Configuration,
            TestResults = buildState.TestResults,
            Coverage = buildState.Coverage
        };

        buildState.History.Insert(0, historyEntry);

        const int MAX_HISTORY_SIZE = 100;
        if (buildState.History.Count > MAX_HISTORY_SIZE)
        {
            buildState.History = buildState.History.Take(MAX_HISTORY_SIZE).ToList();
        }
    }

        private SubsystemBuildState GetOrCreateSubsystemState(string subsystemName)
    {
        var latest = LoadState();
        if (latest.Subsystems.TryGetValue(subsystemName, out var diskState))
        {
            _state.Subsystems[subsystemName] = diskState;
            return diskState;
        }

        if (_state.Subsystems.TryGetValue(subsystemName, out var existingState))
        {
            return existingState;
        }

        return new SubsystemBuildState
        {
            Name = subsystemName
        };
    }

        public SubsystemBuildState? GetSubsystemState(string subsystemName)
    {
        return _state.Subsystems.TryGetValue(subsystemName, out var state) ? state : null;
    }

        public void SaveState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);

            var onDisk = LoadState();

            foreach (var (name, subsystemState) in _state.Subsystems)
            {
                onDisk.Subsystems[name] = subsystemState;
            }

            onDisk.LastUpdated = DateTime.UtcNow;
            _state.LastUpdated = onDisk.LastUpdated;

            if (File.Exists(_stateFilePath))
            {
                File.Copy(_stateFilePath, $"{_stateFilePath}.bak", overwrite: true);
            }

            var json = JsonSerializer.Serialize(onDisk, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var tempPath = $"{_stateFilePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _stateFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            if (_context.Verbose)
            {
                Console.WriteLine($"Warning: Could not save build state: {ex.Message}");
            }
        }
    }

    private BuildState LoadState()
    {
        if (!File.Exists(_stateFilePath))
        {
            return new BuildState();
        }

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<BuildState>(json) ?? new BuildState();

            if (state.SchemaVersion != BuildState.CurrentSchemaVersion)
            {
                state.SchemaVersion = BuildState.CurrentSchemaVersion;
            }

            return state;
        }
        catch (Exception ex)
        {
            var corruptPath = $"{_stateFilePath}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}";
            try
            {
                File.Copy(_stateFilePath, corruptPath, overwrite: true);
            }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
            {
                if (_context.Verbose)
                {
                    Console.WriteLine($"Warning: could not preserve corrupt build state to {corruptPath}: {copyEx.Message}");
                }
            }

            Console.WriteLine($"Warning: build state at {_stateFilePath} is corrupt and was preserved at {corruptPath}: {ex.Message}");
            return new BuildState();
        }
    }

    private Dictionary<string, long> GetSourceFiles(SubsystemDefinition subsystem)
    {
        var sourceFiles = new Dictionary<string, long>();

        if (!Directory.Exists(subsystem.Directory))
        {
            return sourceFiles;
        }

        var patterns = new[] { "*.cs", "*.csproj", "smash.yml", "*.proto" };

        foreach (var pattern in patterns)
        {
            var files = Directory.GetFiles(subsystem.Directory, pattern, SearchOption.AllDirectories)
                .Where(f => !Atelier.Build.Utils.PathSegments.IsUnderBinOrObj(f));

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.Exists)
                {
                    sourceFiles[file] = fileInfo.LastWriteTimeUtc.Ticks;
                }
            }
        }

        return sourceFiles;
    }

    private Dictionary<string, long> GetOutputFiles(SubsystemDefinition subsystem)
    {
        var outputFiles = new Dictionary<string, long>();

        if (string.IsNullOrEmpty(subsystem.Directory))
        {
            return outputFiles;
        }

        var allBinDirs = Directory.GetDirectories(subsystem.Directory, "bin", SearchOption.AllDirectories);

        foreach (var binDir in allBinDirs)
        {

            var configDir = Path.Combine(binDir, subsystem.BuildConfiguration);
            if (Directory.Exists(configDir))
            {
                var dlls = Directory.GetFiles(configDir, "*.dll", SearchOption.AllDirectories);

                foreach (var dll in dlls)
                {
                    var fileInfo = new FileInfo(dll);
                    if (fileInfo.Exists)
                    {
                        outputFiles[dll] = fileInfo.LastWriteTimeUtc.Ticks;
                    }
                }
            }
        }

        return outputFiles;
    }

    private static bool DependenciesMatch(List<string> recorded, IReadOnlyList<string> current)
    {
        if (recorded.Count != current.Count)
        {
            return false;
        }

        var recordedSet = new HashSet<string>(recorded, StringComparer.OrdinalIgnoreCase);
        var currentSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);

        return recordedSet.SetEquals(currentSet);
    }
}
