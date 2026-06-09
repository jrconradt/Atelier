using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Atelier.Build.Utils;
using T = Atelier.Build.Templates.Stubs;

namespace Atelier.Build.Generation;

public class DefaultContextAccessorGenerator
{
    private readonly BuildContext _context;

    public DefaultContextAccessorGenerator(BuildContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateAsync(BoutiqueYamlSchema schema, string outputDirectory)
    {
        var boutiqueName = Naming.ToBoutiqueAssemblyIdentifier(schema.Name);

        var code = new T.DefaultContextAccessor { BoutiqueName = boutiqueName }.Render();
        var outputPath = Path.Combine(outputDirectory, "DefaultContextAccessor.g.cs");
        await File.WriteAllTextAsync(outputPath, code).ConfigureAwait(false);
        return outputPath;
    }
}
