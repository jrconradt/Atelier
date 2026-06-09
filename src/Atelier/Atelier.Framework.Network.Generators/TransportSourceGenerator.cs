using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atelier.Framework.Network.Transport.CodeGen;

[Generator]
public sealed class TransportSourceGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor NonAwaitableTransportMethodRule = new DiagnosticDescriptor(
        "ATELIER0800",
        "Transport interface method must return Task or ValueTask",
        "Method '{0}' on transport interface '{1}' returns '{2}'; the generated transport client and server emit await over the return type and require Task, Task<T>, ValueTask, or ValueTask<T>",
        "Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated transport client awaits the send and the server awaits the implementation call. A non-awaitable return type would produce an async method over a non-awaitable type, which does not compile. Change the method to return Task/Task<T> or ValueTask/ValueTask<T>.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    private static readonly DiagnosticDescriptor MultiParameterTransportMethodRule = new DiagnosticDescriptor(
        "ATELIER0801",
        "Transport interface method must declare at most one non-CancellationToken parameter",
        "Method '{0}' on transport interface '{1}' declares {2} non-CancellationToken parameters; the generated transport places a single payload on the wire and reconstructs a single argument on the server, so additional parameters would be silently dropped",
        "Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated transport client serializes one payload and the server deserializes one argument. A method with two or more non-CancellationToken parameters would compile a client signature that accepts them but transmits only the first. Wrap the parameters in a single request type.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var emissions = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (ctx, _) => Transform(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        context.RegisterSourceOutput(
            emissions,
            static (spc, result) =>
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic.ToDiagnostic());
                }

                foreach (var emission in result.Emissions)
                {
                    spc.AddSource(emission.HintName,
                                  SourceText.From(emission.Source, Encoding.UTF8));
                }
            });
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        return node is InterfaceDeclarationSyntax decl
            && decl.AttributeLists.Count > 0;
    }

    private static TransportEmissions? Transform(GeneratorSyntaxContext ctx)
    {
        var decl = (InterfaceDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(decl) is not INamedTypeSymbol iface)
        {
            return null;
        }

        var compilation = ctx.SemanticModel.Compilation;
        var task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        var taskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        var valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
        var valueTaskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");

        var hasTransportAttribute = false;
        foreach (var attr in iface.GetAttributes())
        {
            if (attr.AttributeClass?.Name is "HttpTransportAttribute" or "InProcessTransportAttribute")
            {
                hasTransportAttribute = true;
                break;
            }
        }

        if (!hasTransportAttribute)
        {
            return null;
        }

        var diagnostics = ImmutableArray.CreateBuilder<TransportDiagnostic>();
        foreach (var method in iface.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary)
            {
                continue;
            }

            var location = method.Locations.FirstOrDefault() ?? iface.Locations.FirstOrDefault() ?? Location.None;

            if (!IsAwaitable(method.ReturnType, task, taskOfT, valueTask, valueTaskOfT))
            {
                diagnostics.Add(TransportDiagnostic.From(
                    NonAwaitableTransportMethodRule,
                    location,
                    method.Name,
                    iface.Name,
                    method.ReturnType.ToDisplayString()));
            }

            var nonCancellationParameters = method.Parameters
                .Count(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken");

            if (nonCancellationParameters > 1)
            {
                diagnostics.Add(TransportDiagnostic.From(
                    MultiParameterTransportMethodRule,
                    location,
                    method.Name,
                    iface.Name,
                    nonCancellationParameters.ToString()));
            }
        }

        if (diagnostics.Count > 0)
        {
            return new TransportEmissions(ImmutableArray<TransportEmission>.Empty, diagnostics.ToImmutable());
        }

        var emissions = ImmutableArray.CreateBuilder<TransportEmission>();

        foreach (var attr in iface.GetAttributes())
        {
            switch (attr.AttributeClass?.Name)
            {
                case "HttpTransportAttribute":
                    AddEmission(emissions, iface, new HttpClientGenerator(iface));
                    AddEmission(emissions, iface, new HttpServerGenerator(iface));
                    break;
                case "InProcessTransportAttribute":
                    AddEmission(emissions, iface, new InProcessClientGenerator(iface));
                    break;
            }
        }

        if (emissions.Count == 0)
        {
            return null;
        }

        return new TransportEmissions(emissions.ToImmutable(), ImmutableArray<TransportDiagnostic>.Empty);
    }

    private static bool IsAwaitable(
        ITypeSymbol returnType,
        INamedTypeSymbol? task,
        INamedTypeSymbol? taskOfT,
        INamedTypeSymbol? valueTask,
        INamedTypeSymbol? valueTaskOfT)
    {
        if (returnType is not INamedTypeSymbol named)
        {
            return false;
        }

        return Matches(named, task)
            || Matches(named.OriginalDefinition, taskOfT)
            || Matches(named, valueTask)
            || Matches(named.OriginalDefinition, valueTaskOfT);
    }

    private static bool Matches(ITypeSymbol candidate, INamedTypeSymbol? known)
    {
        return known is not null
            && SymbolEqualityComparer.Default.Equals(candidate, known);
    }

    private static void AddEmission(
        ImmutableArray<TransportEmission>.Builder emissions,
        INamedTypeSymbol iface,
        TransportClientGenerator gen)
    {
        var source = gen.Render();
        if (!string.IsNullOrEmpty(source))
        {
            emissions.Add(new TransportEmission(iface.Name + "_" + gen.Variant + ".g.cs", source));
        }
    }

    private static void AddEmission(
        ImmutableArray<TransportEmission>.Builder emissions,
        INamedTypeSymbol iface,
        TransportServerGenerator gen)
    {
        var source = gen.Render();
        if (!string.IsNullOrEmpty(source))
        {
            emissions.Add(new TransportEmission(iface.Name + "_" + gen.Variant + ".g.cs", source));
        }
    }
}

internal sealed record TransportEmission(string HintName, string Source);

internal sealed record TransportEmissions(
    ImmutableArray<TransportEmission> Emissions,
    ImmutableArray<TransportDiagnostic> Diagnostics);

internal sealed record TransportDiagnostic(
    DiagnosticDescriptor Descriptor,
    string? FilePath,
    TextSpan TextSpan,
    LinePositionSpan LineSpan,
    EquatableArray<string> MessageArgs)
{
    public static TransportDiagnostic From(
        DiagnosticDescriptor descriptor,
        Location location,
        params string[] messageArgs)
    {
        var lineSpan = location.GetLineSpan();
        return new TransportDiagnostic(
            descriptor,
            location.SourceTree?.FilePath ?? lineSpan.Path,
            location.SourceSpan,
            lineSpan.Span,
            new EquatableArray<string>(messageArgs.ToImmutableArray()));
    }

    public Diagnostic ToDiagnostic()
    {
        var location = FilePath is null
            ? Location.None
            : Location.Create(FilePath, TextSpan, LineSpan);
        return Diagnostic.Create(Descriptor, location, MessageArgs.Items.ToArray());
    }
}

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    public ImmutableArray<T> Items { get; }

    public EquatableArray(ImmutableArray<T> items)
    {
        Items = items.IsDefault ? ImmutableArray<T>.Empty : items;
    }

    public bool Equals(EquatableArray<T> other)
    {
        return Items.SequenceEqual(other.Items);
    }

    public override bool Equals(object? obj)
    {
        return obj is EquatableArray<T> other
            && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in Items)
        {
            hash = (hash * 31) + item.GetHashCode();
        }

        return hash;
    }
}
