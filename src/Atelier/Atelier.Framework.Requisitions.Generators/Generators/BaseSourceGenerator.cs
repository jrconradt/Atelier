using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Atelier.Framework.Generators.ConflictResolution;

public abstract class BaseSourceGenerator : IIncrementalGenerator
{
    public abstract void Initialize(IncrementalGeneratorInitializationContext context);

    protected void AddSource(SourceProductionContext context, string fileName, string source)
    {
        context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
    }
}
