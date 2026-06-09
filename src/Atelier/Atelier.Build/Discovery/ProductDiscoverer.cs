using Atelier.Build.Pipeline;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Atelier.Build.Discovery;

public class ProductDiscoverer
{
    private readonly BuildContext _context;

    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public ProductDiscoverer(BuildContext context)
    {
        _context = context;
    }

        public async Task<IReadOnlyList<ProductDefinition>> DiscoverAsync()
    {
        var srcDir = Path.Combine(_context.SolutionRoot, "src");
        if (!Directory.Exists(srcDir))
        {
            return [];
        }

        var productFiles = Directory.GetFiles(srcDir, "product.yml", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        var definitions = new List<ProductDefinition>();

        if (_context.Verbose || productFiles.Length > 0)
        {
            Spectre.Console.AnsiConsole.MarkupLine($"[yellow]>>> DiscoverProductsAsync: Found {productFiles.Length} product.yml files in src/[/]");
        }

        foreach (var filePath in productFiles)
        {
            if (_context.Verbose)
            {
                Spectre.Console.AnsiConsole.MarkupLine($"[yellow]>>> Parsing product: {filePath}[/]");
            }
            var definition = await ParseProductAsync(filePath).ConfigureAwait(false);
            if (definition != null)
            {
                definitions.Add(definition);
                Spectre.Console.AnsiConsole.MarkupLine($"[green]>>> Added product: {definition.Name}[/]");
            }
        }

        return definitions;
    }

        private async Task<ProductDefinition?> ParseProductAsync(string filePath)
    {
        var yamlContent = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        var schema = _yamlDeserializer.Deserialize<MinimalProductSchema>(yamlContent);

        var directory = Path.GetDirectoryName(filePath)!;
        var productDirName = Path.GetFileName(directory);

        var productName = !string.IsNullOrEmpty(schema.Name) ? schema.Name : productDirName;
        var artifactName = $"atelier-{productName}";

        var projectReferences = await DiscoverProjectReferencesAsync(directory, productDirName).ConfigureAwait(false);

        string? solutionPath = null;
        var slnFiles = Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly);
        if (slnFiles.Length > 0)
        {
            solutionPath = slnFiles[0];
        }

        if (_context.Verbose)
        {
            Spectre.Console.AnsiConsole.MarkupLine($"[cyan]  Product: {productName}, Projects: {projectReferences.Count}[/]");
        }

        return new ProductDefinition
        {
            Name = artifactName,
            Version = schema.Version,
            Description = schema.Description ?? $"{productName} Library Collection",
            SourceDirectory = directory,
            ProductName = productDirName,
            Dependencies = schema.Dependencies ?? [],
            ProjectReferences = projectReferences,
            SolutionPath = solutionPath,
            Build = new ProductBuildSettings
            {
                Configuration = schema.Build?.Configuration ?? "Release",
                TreatWarningsAsErrors = schema.Build?.TreatWarningsAsErrors ?? false,
                AdditionalMsBuildArgs = schema.Build?.MsBuildArgs ?? []
            }
        };
    }

        private Task<IReadOnlyList<string>> DiscoverProjectReferencesAsync(string productDirectory, string productName)
    {
        var projects = new List<string>();
        var srcDir = Path.GetDirectoryName(productDirectory)!;

        var allCsproj = Directory.GetFiles(srcDir, "*.csproj", SearchOption.AllDirectories);

        foreach (var csproj in allCsproj)
        {
            var relative = Path.GetRelativePath(srcDir, csproj);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (segments.Length != 2 && segments.Length != 3)
            {
                continue;
            }

            var topDirName = segments[0];
            if (!topDirName.StartsWith(productName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (topDirName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                || topDirName.EndsWith(".Benchmarks", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (segments.Length == 3)
            {
                var subDirName = segments[1];
                if (subDirName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                    || subDirName.EndsWith(".Benchmarks", StringComparison.OrdinalIgnoreCase)
                    || subDirName.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || subDirName.Equals("obj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var projectName = Path.GetFileNameWithoutExtension(csproj);

            if (projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                || projectName.EndsWith(".Benchmarks", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            projects.Add(csproj);

            if (_context.Verbose)
            {
                Spectre.Console.AnsiConsole.MarkupLine($"[green]    ✓ {projectName}[/]");
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(projects);
    }
}
