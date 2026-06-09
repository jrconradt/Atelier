using System.Collections.Immutable;
using Atelier.Framework.Generators.ConflictResolution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atelier.Framework.Generators.Requisition;

[Generator]
public sealed class FactorySourceGenerator : BaseSourceGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var factories = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (ctx, _) => Transform(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!)
            .Collect();

        context.RegisterSourceOutput(
            factories,
            (spc, results) => Emit(spc, results));
    }

    private void Emit(SourceProductionContext context, ImmutableArray<FactoryTransformResult> results)
    {
        var registrations = new List<RegistrationInfo>();

        foreach (var result in results)
        {
            registrations.Add(result.Registration);

            AddSource(context, result.FactoryFileName, result.FactoryCode);

            if (result.PoolFileName is not null && result.PoolCode is not null)
            {
                AddSource(context, result.PoolFileName, result.PoolCode);
            }
        }

        if (registrations.Count > 0)
        {
            var registrationCode = GenerateRegistrationCode(registrations);
            AddSource(context, "RequisitionServiceRegistration.g.cs", registrationCode);
        }
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax and not StructDeclarationSyntax)
        {
            return false;
        }

        var typeDeclaration = (TypeDeclarationSyntax)node;
        return typeDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr =>
            {
                var name = attr.Name.ToString();
                return name == "Transient" || name == "Scoped" || name == "Singleton" ||
                       name == "Pooled" || name == "ValueObject";
            });
    }

    private static FactoryTransformResult? Transform(GeneratorSyntaxContext ctx)
    {
        var typeDeclaration = (TypeDeclarationSyntax)ctx.Node;
        var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(typeDeclaration);

        if (typeSymbol is null)
        {
            return null;
        }

        foreach (var reference in typeSymbol.DeclaringSyntaxReferences)
        {
            var referenceSyntax = reference.GetSyntax();
            if (!IsCandidate(referenceSyntax))
            {
                continue;
            }
            if (!ReferenceEquals(referenceSyntax, typeDeclaration))
            {
                return null;
            }
            break;
        }

        var lifecycleInfo = GetLifecycleInfo(typeSymbol);
        if (lifecycleInfo is null)
        {
            return null;
        }

        var factoryCode = GenerateFactoryCode(typeSymbol, lifecycleInfo, ctx.SemanticModel.Compilation);
        var factoryFileName = $"{typeSymbol.Name}Factory.g.cs";

        string? poolFileName = null;
        string? poolCode = null;

        if (lifecycleInfo.IsPooled)
        {
            poolCode = GeneratePoolCode(typeSymbol, lifecycleInfo);
            poolFileName = $"{typeSymbol.Name}Pool.g.cs";
        }

        var registration = new RegistrationInfo(
            typeSymbol.Name,
            typeSymbol.ToDisplayString(),
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            typeSymbol.ContainingNamespace.ToDisplayString(),
            lifecycleInfo.Lifecycle);

        return new FactoryTransformResult(
            registration,
            factoryFileName,
            factoryCode,
            poolFileName,
            poolCode);
    }

    private static FactoryTypeInfo? GetLifecycleInfo(INamedTypeSymbol typeSymbol)
    {
        var attributes = typeSymbol.GetAttributes();

        foreach (var attr in attributes)
        {
            var attrName = attr.AttributeClass?.Name;

            switch (attrName)
            {
                case "TransientAttribute":
                {
                    return new FactoryTypeInfo
                    {
                        TypeSymbol = typeSymbol,
                        Lifecycle = LifecycleType.Transient,
                        IsPooled = false
                    };
                }

                case "ScopedAttribute":
                {
                    return new FactoryTypeInfo
                    {
                        TypeSymbol = typeSymbol,
                        Lifecycle = LifecycleType.Scoped,
                        IsPooled = false
                    };
                }

                case "SingletonAttribute":
                {
                    return new FactoryTypeInfo
                    {
                        TypeSymbol = typeSymbol,
                        Lifecycle = LifecycleType.Singleton,
                        IsPooled = false
                    };
                }

                case "PooledAttribute":
                {
                    var maxSize = 100;
                    var initialSize = 10;

                    foreach (var namedArg in attr.NamedArguments)
                    {
                        if (namedArg.Key == "MaxSize" && namedArg.Value.Value is int max)
                        {
                            maxSize = max;
                        }

                        if (namedArg.Key == "InitialSize" && namedArg.Value.Value is int initial)
                        {
                            initialSize = initial;
                        }
                    }

                    return new FactoryTypeInfo
                    {
                        TypeSymbol = typeSymbol,
                        Lifecycle = LifecycleType.Transient,
                        IsPooled = true,
                        MaxPoolSize = maxSize,
                        InitialPoolSize = initialSize
                    };
                }

                case "ValueObjectAttribute":
                {
                    return new FactoryTypeInfo
                    {
                        TypeSymbol = typeSymbol,
                        Lifecycle = LifecycleType.Transient,
                        IsPooled = true,
                        MaxPoolSize = 100,
                        InitialPoolSize = 10
                    };
                }
            }
        }

        return null;
    }

    private static string GenerateFactoryCode(
        INamedTypeSymbol typeSymbol,
        FactoryTypeInfo lifecycleInfo,
        Compilation compilation)
    {
        var builder = new FactoryCodeBuilder(typeSymbol, lifecycleInfo);
        return builder.Build();
    }

    private static string GeneratePoolCode(INamedTypeSymbol typeSymbol, FactoryTypeInfo lifecycleInfo)
    {
        var builder = new PoolCodeBuilder(typeSymbol, lifecycleInfo);
        return builder.Build();
    }

    private static string GenerateRegistrationCode(List<RegistrationInfo> registrations)
    {
        var builder = new RegistrationCodeBuilder(registrations);
        return builder.Build();
    }
}

internal sealed record RegistrationInfo(
    string TypeName,
    string FullTypeName,
    string FullyQualifiedTypeName,
    string Namespace,
    LifecycleType Lifecycle);

internal sealed record FactoryTransformResult(
    RegistrationInfo Registration,
    string FactoryFileName,
    string FactoryCode,
    string? PoolFileName,
    string? PoolCode);
