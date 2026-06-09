using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnenforcedAuthorizationMetadataAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER0730";

    private static readonly DiagnosticDescriptor UnenforcedMetadataRule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Authorization metadata is declared where nothing enforces it",
        "'{0}' is applied to '{1}' but no generator or runtime path reads it, so the authorization requirement is silently ignored. Remove the attribute or route the surface through an enforced authorization path ([RequiresAuthorization] Roles/Permissions on a transport operation, [Facility] gateway claims/scopes, or [Api] claims).",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[RequiresClaim], [RequiresClaimContract], [Grpc(claims)], and the Policy/Action/Resource members of [RequiresAuthorization] advertise an authorization requirement that no generator or runtime path enforces. Declaring them produces an unenforced, false-sense-of-security gate; they are refused at compile time.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    private static readonly string[] UnenforcedAuthorizationMembers = { "Policy", "Action", "Resource" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UnenforcedMetadataRule);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType, SymbolKind.Method);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        foreach (var attribute in context.Symbol.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.Name;
            var descriptor = DescribeUnenforcedMetadata(attributeName, attribute);
            if (descriptor == null)
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                ?? context.Symbol.Locations.FirstOrDefault()
                ?? Location.None;

            context.ReportDiagnostic(Diagnostic.Create(
                UnenforcedMetadataRule,
                location,
                descriptor,
                context.Symbol.Name));
        }
    }

    private static string? DescribeUnenforcedMetadata(string? attributeName, AttributeData attribute)
    {
        if (attributeName == "RequiresClaimAttribute"
            || attributeName == "RequiresClaimContractAttribute")
        {
            return attributeName;
        }

        if (attributeName == "GrpcAttribute")
        {
            return HasClaims(attribute) ? "GrpcAttribute claims" : null;
        }

        if (attributeName == "RequiresAuthorizationAttribute")
        {
            var unenforced = UnenforcedAuthorizationMembers
                .Where(member => HasNonEmptyMember(attribute, member))
                .ToArray();

            if (unenforced.Length > 0)
            {
                return $"RequiresAuthorizationAttribute.{string.Join("/", unenforced)}";
            }
        }

        return null;
    }

    private static bool HasNonEmptyMember(AttributeData attribute, string memberName)
    {
        var value = attribute.NamedArguments
            .FirstOrDefault(na => na.Key == memberName)
            .Value;

        return !value.IsNull
            && !string.IsNullOrEmpty(value.Value?.ToString());
    }

    private static bool HasClaims(AttributeData attribute)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Kind == TypedConstantKind.Array
                && !argument.IsNull
                && argument.Values.Any(v => !string.IsNullOrEmpty(v.Value?.ToString())))
            {
                return true;
            }
        }

        var claimsNamed = attribute.NamedArguments
            .FirstOrDefault(na => na.Key == "Claims")
            .Value;

        return claimsNamed.Kind == TypedConstantKind.Array
            && !claimsNamed.IsNull
            && claimsNamed.Values.Any(v => !string.IsNullOrEmpty(v.Value?.ToString()));
    }
}
