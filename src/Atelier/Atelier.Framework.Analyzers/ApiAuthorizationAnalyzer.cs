using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ApiAuthorizationAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "Security";

    private static readonly DiagnosticDescriptor UnprotectedApiMethodRule = new DiagnosticDescriptor(
        "ATELIER0710",
        "API method has no authorization or anonymous opt-out",
        "Public method '{0}' on [Api] class '{1}' declares no authorization claims and is not marked [AllowAnonymous]. Add claims to [Api], or mark the method or class [AllowAnonymous] to expose it intentionally.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every public method exposed by an [Api] class must either be covered by declared authorization claims or be explicitly opted out with [AllowAnonymous]; otherwise it ships as an unprotected endpoint.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    private static readonly DiagnosticDescriptor AuthBypassFacilityMethodRule = new DiagnosticDescriptor(
        "ATELIER0720",
        "Authenticated facility method would bypass AuthorizeAsync",
        "Method '{0}' on [Facility(RequiresAuthentication = true)] interface '{1}' does not return Outcome or Outcome<T>; the generated gateway would call the backend directly, bypassing AuthorizeAsync. Change the return type to Outcome/Outcome<T> or disable authentication on the facility.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Facility interfaces that require authentication must expose only Outcome-returning methods so the generated gateway routes every call through AuthorizeAsync.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            UnprotectedApiMethodRule,
            AuthBypassFacilityMethodRule);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeApiClass, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeFacilityInterface, SyntaxKind.InterfaceDeclaration);
    }

    private void AnalyzeApiClass(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null)
        {
            return;
        }

        var apiAttribute = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "ApiAttribute");

        if (apiAttribute == null)
        {
            return;
        }

        if (HasAllowAnonymous(classSymbol))
        {
            return;
        }

        if (HasDeclaredClaims(apiAttribute))
        {
            return;
        }

        foreach (var member in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(member);
            if (methodSymbol == null || !IsExposedApiMethod(methodSymbol))
            {
                continue;
            }

            if (HasAllowAnonymous(methodSymbol))
            {
                continue;
            }

            var diagnostic = Diagnostic.Create(
                UnprotectedApiMethodRule,
                member.Identifier.GetLocation(),
                methodSymbol.Name,
                classSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private void AnalyzeFacilityInterface(SyntaxNodeAnalysisContext context)
    {
        var interfaceDeclaration = (InterfaceDeclarationSyntax)context.Node;
        var interfaceSymbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration);

        if (interfaceSymbol == null)
        {
            return;
        }

        var facilityAttribute = interfaceSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "FacilityAttribute");

        if (facilityAttribute == null)
        {
            return;
        }

        if (!RequiresAuthentication(facilityAttribute))
        {
            return;
        }

        foreach (var member in interfaceDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(member);
            if (methodSymbol == null || methodSymbol.MethodKind != MethodKind.Ordinary)
            {
                continue;
            }

            if (IsOutcomeReturn(methodSymbol.ReturnType))
            {
                continue;
            }

            var diagnostic = Diagnostic.Create(
                AuthBypassFacilityMethodRule,
                member.Identifier.GetLocation(),
                methodSymbol.Name,
                interfaceSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool HasAllowAnonymous(ISymbol symbol)
    {
        return symbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "AllowAnonymousAttribute");
    }

    private static bool HasDeclaredClaims(AttributeData apiAttribute)
    {
        foreach (var argument in apiAttribute.ConstructorArguments)
        {
            if (argument.Kind == TypedConstantKind.Array
                && !argument.IsNull
                && argument.Values.Any(v => !string.IsNullOrEmpty(v.Value?.ToString())))
            {
                return true;
            }
        }

        var claimsNamed = apiAttribute.NamedArguments
            .FirstOrDefault(na => na.Key == "Claims")
            .Value;

        if (claimsNamed.Kind == TypedConstantKind.Array
            && !claimsNamed.IsNull
            && claimsNamed.Values.Any(v => !string.IsNullOrEmpty(v.Value?.ToString())))
        {
            return true;
        }

        return false;
    }

    private static bool RequiresAuthentication(AttributeData facilityAttribute)
    {
        var requiresAuthentication = true;
        var allowAnonymous = false;

        foreach (var argument in facilityAttribute.NamedArguments)
        {
            if (argument.Key == "RequiresAuthentication" && argument.Value.Value is bool requires)
            {
                requiresAuthentication = requires;
            }
            else if (argument.Key == "AllowAnonymous" && argument.Value.Value is bool anonymous)
            {
                allowAnonymous = anonymous;
            }
        }

        return requiresAuthentication && !allowAnonymous;
    }

    private static bool IsExposedApiMethod(IMethodSymbol method)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (method.MethodKind != MethodKind.Ordinary)
        {
            return false;
        }

        if (method.IsStatic)
        {
            return false;
        }

        if (method.ReturnsVoid)
        {
            return false;
        }

        return method.ReturnType.Name.Contains("Task");
    }

    private static bool IsOutcomeReturn(ITypeSymbol returnType)
    {
        var unwrapped = UnwrapTask(returnType);
        return unwrapped is INamedTypeSymbol named
            && named.Name == "Outcome";
    }

    private static ITypeSymbol UnwrapTask(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && (named.ConstructedFrom.Name == "Task" || named.ConstructedFrom.Name == "ValueTask"))
        {
            return named.TypeArguments[0];
        }

        return type;
    }
}
