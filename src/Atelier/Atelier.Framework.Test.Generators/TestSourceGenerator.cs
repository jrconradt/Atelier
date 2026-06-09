using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Templar.Rendering;
using Templar.Presets;

namespace Atelier.Framework.Test.Generators;

[Generator]
public sealed class TestSourceGenerator : IIncrementalGenerator
{
    private static readonly HashSet<string> RelevantAttributeNames = new()
    {
        "Requisite",
        "RequisiteAttribute",
        "Runtime",
        "RuntimeAttribute",
        "Operation",
        "OperationAttribute",
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var sidecars = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (ctx, _) => Transform(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        context.RegisterSourceOutput(
            sidecars,
            static (spc, result) =>
                spc.AddSource(result.HintName,
                              SourceText.From(result.Source, Encoding.UTF8)));
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax cls)
        {
            return false;
        }
        return cls.BaseList is not null
            || cls.Members.Any(HasRelevantAttribute);
    }

    private static TestSidecarResult? Transform(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        foreach (var declaration in symbol.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax() != classDecl)
            {
                return null;
            }
            break;
        }

        var metadata = ConsumerAnalyzer.Analyze(symbol, ctx.SemanticModel.Compilation);
        if (metadata is null || !metadata.HasAnyTestableSurface)
        {
            return null;
        }

        var asmName = symbol.ContainingAssembly?.Name ?? string.Empty;
        if (asmName.EndsWith(".Generators")
            || asmName == "Atelier.Framework.Testing")
        {
            return null;
        }

        var source = EmitSidecar(metadata);
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        var namespacePart = metadata.Namespace.Replace(".", "_");
        return new TestSidecarResult(
            $"{namespacePart}_{metadata.ClassName}_GeneratedTests.g.cs",
            source);
    }

    private static bool HasRelevantAttribute(MemberDeclarationSyntax member)
    {
        foreach (var list in member.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                if (RelevantAttributeNames.Contains(SimpleName(attribute.Name)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static string SimpleName(NameSyntax name)
    {
        var text = name.ToString();
        var lastDot = text.LastIndexOf('.');
        return lastDot >= 0 ? text.Substring(lastDot + 1) : text;
    }

    private static string EmitSidecar(ConsumerMetadata m)
    {
        var tests = Sequence.Lines(WiringTestEmitter.Emit(m)
            .Concat(AtelierTestEmitter.Emit(m))
            .Concat(OperationTestEmitter.Emit(m))
            .Concat(LifecycleTestEmitter.Emit(m)));

        var emitsOutcomeShapeTest =
            m.Operations.Count > 0
            && m.RequisiteFields.Count > 0
            && m.GeneratorEmitsConstructor;

        var body = new Templates.TestSidecarBody
        {
            ClassName = m.ClassName,
            Tests = tests,
            OutcomeHelper = emitsOutcomeShapeTest
                ? new Templates.Operation.OutcomeShape()
                : null,
        };

        return new CSharpFile
        {
            Namespace = m.Namespace,
            Usings = new[]
            {
                "global::System",
                "global::System.Collections.Generic",
                "global::System.Linq",
                "global::System.Reflection",
                "global::System.Threading",
                "global::System.Threading.Tasks",
                "global::Atelier.Framework.Testing",
            },
            Body = body.Render(),
        }.Render();
    }
}

internal sealed record TestSidecarResult(string HintName, string Source);
