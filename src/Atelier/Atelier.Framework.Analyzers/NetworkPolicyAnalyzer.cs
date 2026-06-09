using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NetworkPolicyAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "Network";

    private static readonly DiagnosticDescriptor MissingNetworkZoneRule = new DiagnosticDescriptor(
        "ATELIER0300",
        "Missing Network Zone",
        "Service '{0}' has no [NetworkZone] attribute; its zone is inferred from namespace. Declare [NetworkZone(...)] to make the security boundary explicit.",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Service types should declare an explicit [NetworkZone] for their security boundary. Until declared, the zone is inferred from namespace, which is heuristic. Promote this to an error (remove ATELIER0300 from WarningsNotAsErrors) to force explicit declaration once zones are assigned.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    private static readonly DiagnosticDescriptor NetworkIsolationRule = new DiagnosticDescriptor(
        "ATELIER0310",
        "Network Policy Violation",
        "Service '{0}' in zone '{1}' cannot communicate with service '{2}' in zone '{3}'. Allowed outbound zones: {4}.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Services must respect network zone boundaries defined by [NetworkZone] attributes.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    private static readonly DiagnosticDescriptor UnencryptedCommunicationRule = new DiagnosticDescriptor(
        "ATELIER0320",
        "Unencrypted Service Communication",
        "Service '{0}' communicates with '{1}' without encryption. Set RequiresEncryption=true in [ServiceDependency].",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Service-to-service communication should be encrypted.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            MissingNetworkZoneRule,
            NetworkIsolationRule,
            UnencryptedCommunicationRule);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeServiceDeclaration, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeServiceDependency, SyntaxKind.PropertyDeclaration);
    }

    private void AnalyzeServiceDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null || !IsServiceClass(classSymbol))
        {
            return;
        }

        var networkZoneAttribute = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "NetworkZoneAttribute");

        if (networkZoneAttribute == null)
        {
            var diagnostic = Diagnostic.Create(
                MissingNetworkZoneRule,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name);
            context.ReportDiagnostic(diagnostic);
            return;
        }

        AnalyzeZoneIsolation(context, classDeclaration, classSymbol, networkZoneAttribute);
    }

    private static void AnalyzeZoneIsolation(
        SyntaxNodeAnalysisContext context,
        ClassDeclarationSyntax classDeclaration,
        INamedTypeSymbol classSymbol,
        AttributeData networkZoneAttribute)
    {
        var sourceZone = ReadZone(networkZoneAttribute);
        if (sourceZone == null)
        {
            return;
        }

        var allowedOutbound = ReadAllowedOutboundZones(networkZoneAttribute);

        foreach (var member in classSymbol.GetMembers())
        {
            if (!HasRequisiteAttribute(member))
            {
                continue;
            }

            ITypeSymbol? dependencyType = member switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };

            if (dependencyType is not INamedTypeSymbol namedDependency)
            {
                continue;
            }

            var dependencyZoneAttribute = namedDependency.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "NetworkZoneAttribute");
            if (dependencyZoneAttribute == null)
            {
                continue;
            }

            var targetZone = ReadZone(dependencyZoneAttribute);
            if (targetZone == null
                || targetZone == sourceZone
                || allowedOutbound.Contains(targetZone))
            {
                continue;
            }

            var location = member.Locations.FirstOrDefault() ?? classDeclaration.Identifier.GetLocation();
            var allowedText = allowedOutbound.Count == 0
                ? "(none)"
                : string.Join(", ", allowedOutbound);

            var diagnostic = Diagnostic.Create(
                NetworkIsolationRule,
                location,
                classSymbol.Name,
                sourceZone,
                namedDependency.Name,
                targetZone,
                allowedText);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static string? ReadZone(AttributeData networkZoneAttribute)
    {
        if (networkZoneAttribute.ConstructorArguments.Length == 0)
        {
            return null;
        }

        return ZoneName(networkZoneAttribute.ConstructorArguments[0]);
    }

    private static HashSet<string> ReadAllowedOutboundZones(AttributeData networkZoneAttribute)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        TypedConstant outbound = default;
        var found = false;
        foreach (var named in networkZoneAttribute.NamedArguments)
        {
            if (named.Key == "AllowedOutboundZones")
            {
                outbound = named.Value;
                found = true;
                break;
            }
        }

        if (!found
            && networkZoneAttribute.ConstructorArguments.Length >= 3)
        {
            outbound = networkZoneAttribute.ConstructorArguments[2];
            found = true;
        }

        if (!found
            || outbound.Kind != TypedConstantKind.Array
            || outbound.IsNull)
        {
            return result;
        }

        foreach (var element in outbound.Values)
        {
            var name = ZoneName(element);
            if (name != null)
            {
                result.Add(name);
            }
        }

        return result;
    }

    private static string? ZoneName(TypedConstant zoneConstant)
    {
        if (zoneConstant.Value == null
            || zoneConstant.Type is not INamedTypeSymbol enumType)
        {
            return null;
        }

        var name = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, zoneConstant.Value))
            ?.Name;

        return string.IsNullOrEmpty(name) ? zoneConstant.Value.ToString() : name;
    }

    private static bool HasRequisiteAttribute(ISymbol symbol)
    {
        return symbol.HasAttribute("RequisiteAttribute");
    }

    private void AnalyzeServiceDependency(SyntaxNodeAnalysisContext context)
    {
        var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;
        var propertySymbol = context.SemanticModel.GetSymbolInfo(propertyDeclaration.Type).Symbol as INamedTypeSymbol;

        if (propertySymbol == null || !IsServiceDependency(propertySymbol))
        {
            return;
        }

        var serviceDependencyAttribute = propertySymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "ServiceDependencyAttribute");

        if (serviceDependencyAttribute == null)
        {
            return;
        }

        var requiresEncryptionValue = serviceDependencyAttribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "RequiresEncryption").Value.Value;
        var requiresEncryption = requiresEncryptionValue is bool flag && flag;

        if (!requiresEncryption)
        {
            var containingType = context.ContainingSymbol?.ContainingType
                ?? context.SemanticModel.GetDeclaredSymbol(propertyDeclaration)?.ContainingType;
            var sourceName = containingType?.Name ?? "Service";

            var diagnostic = Diagnostic.Create(
                UnencryptedCommunicationRule,
                propertyDeclaration.GetLocation(),
                sourceName,
                propertySymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsServiceClass(INamedTypeSymbol classSymbol)
    {
        if (classSymbol == null
            || classSymbol.IsAbstract)
        {
            return false;
        }

        if (classSymbol.HasAttribute("InfrastructureAttribute")
            || classSymbol.HasAttribute("ServiceDiscoveryAttribute"))
        {
            return true;
        }

        return classSymbol.AllInterfaces.Any(i => i.Name == "IService");
    }

    private static bool IsServiceDependency(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.HasAttribute("ServiceDependencyAttribute");
    }
}
