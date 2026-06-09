using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Atelier.Build.Utils;
using Templar.Rendering;
using T = Atelier.Build.Templates.Stubs;

namespace Atelier.Build.Generation;

public class StubServicesGenerator
{
    private readonly BuildContext _context;

    public StubServicesGenerator(BuildContext context)
    {
        _context = context;
    }

    public async Task<List<string>> GenerateAsync(BoutiqueYamlSchema schema, string outputDirectory)
    {
        var boutiqueName = Naming.ToBoutiqueAssemblyIdentifier(schema.Name);

        var noOp = await WriteAsync(
            new T.NoOpOffsetManager { BoutiqueName = boutiqueName }.Render(),
            "NoOpOffsetManagerService.g.cs",
            outputDirectory).ConfigureAwait(false);

        var nullProvider = await WriteAsync(
            new T.NullOfferingProvider { BoutiqueName = boutiqueName }.Render(),
            "NullOfferingProvider.g.cs",
            outputDirectory).ConfigureAwait(false);

        var progExt = await WriteAsync(
            new T.ProgramExtensions { BoutiqueName = boutiqueName }.Render(),
            "ProgramExtensions.g.cs",
            outputDirectory).ConfigureAwait(false);

        return new List<string> { noOp, nullProvider, progExt };
    }

    private static async Task<string> WriteAsync(string code, string outputFileName, string outputDirectory)
    {
        var outputPath = Path.Combine(outputDirectory, outputFileName);
        await File.WriteAllTextAsync(outputPath, code).ConfigureAwait(false);
        return outputPath;
    }
}
