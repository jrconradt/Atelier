using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractSecretMemberAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER0740";

    private static readonly DiagnosticDescriptor SecretMemberRule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Secret-bearing contract member must be marked [JsonIgnore]",
        "'{0}.{1}' is a secret-bearing member on a [Contract] type but is not marked [JsonIgnore], so it is serialized in cleartext. Mark it [JsonIgnore] or move the secret off the serialized surface.",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Members whose names indicate a token, secret, password, credential, or key on a [Contract] type are emitted in cleartext by JsonContractSerializer. They must be marked [JsonIgnore] so the secret never crosses a serialized surface (wire, audit, log).",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    private static readonly string[] SecretMemberTokens =
    {
        "Token",
        "Secret",
        "Password",
        "Credential",
        "PrivateKey",
        "ApiKey",
        "AccessKey",
        "SigningKey"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(SecretMemberRule);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (!HasContractAttribute(type))
        {
            return;
        }

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol property)
            {
                continue;
            }

            if (property.DeclaredAccessibility != Accessibility.Public || property.IsStatic
                || property.IsIndexer)
            {
                continue;
            }

            if (!IsSecretMemberName(property.Name))
            {
                continue;
            }

            if (!CarriesRawSecretValue(property.Type))
            {
                continue;
            }

            if (HasJsonIgnore(property))
            {
                continue;
            }

            var location = property.Locations.FirstOrDefault() ?? Location.None;

            context.ReportDiagnostic(Diagnostic.Create(
                SecretMemberRule,
                location,
                type.Name,
                property.Name));
        }
    }

    private static bool HasContractAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "ContractAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static bool CarriesRawSecretValue(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return array.ElementType.SpecialType == SpecialType.System_Byte || array.ElementType.SpecialType == SpecialType.System_Char
                || array.ElementType.SpecialType == SpecialType.System_String;
        }

        if (type is INamedTypeSymbol named
            && named.IsGenericType)
        {
            foreach (var argument in named.TypeArguments)
            {
                if (argument.SpecialType == SpecialType.System_String)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static readonly string[] MetadataSuffixes =
    {
        "Claims",
        "Reference",
        "Type",
        "Name",
        "Id",
        "Kind"
    };

    private static bool IsSecretMemberName(string name)
    {
        foreach (var suffix in MetadataSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        foreach (var token in SecretMemberTokens)
        {
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasJsonIgnore(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "JsonIgnoreAttribute")
            {
                return true;
            }
        }

        return false;
    }
}
