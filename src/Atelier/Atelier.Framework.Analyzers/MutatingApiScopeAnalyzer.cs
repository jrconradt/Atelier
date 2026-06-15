using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MutatingApiScopeAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER0750";

    private static readonly string[] ReaderPrefixes = new[]
    {
        "get",
        "fetch",
        "retrieve",
        "discover",
        "find",
        "list",
        "query",
        "search",
    };

    private static readonly string[] MutatorTokens = new[]
    {
        "create",
        "add",
        "insert",
        "register",
        "publish",
        "submit",
        "send",
        "post",
        "start",
        "begin",
        "invoke",
        "execute",
        "handle",
        "update",
        "modify",
        "edit",
        "replace",
        "set",
        "delete",
        "remove",
        "unregister",
        "release",
        "revoke",
        "stop",
        "cancel",
        "patch",
        "purge",
        "archive",
        "reset",
        "provision",
        "furnish",
    };

    private static readonly string[] ConjunctionTokens = new[]
    {
        "or",
        "and",
        "then",
    };

    private static readonly DiagnosticDescriptor MutatingApiWithoutWriteScopeRule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Mutating API operation has no write-tier scope",
        "Mutating method '{0}' on [Api] class '{1}' declares no write-tier authorization scope. Add [ScopeResource(typeof(...))] to the class or interface so the write scope is derived, or an explicit [RequiresScope] on the method, or mark the method [AllowAnonymous] to expose it unprotected.",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A public mutating operation exposed by an [Api] class must carry a write-tier scope. The scope is either derived from a [ScopeResource] binding on the declaring type (write tier inferred from the mutating method name) or supplied explicitly with [RequiresScope]. A mutating operation with neither would ship as an unprotected write surface; it is refused at compile time.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MutatingApiWithoutWriteScopeRule);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeApiClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeApiClass(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null)
        {
            return;
        }

        if (!HasAttribute(classSymbol, "ApiAttribute"))
        {
            return;
        }

        if (HasAttribute(classSymbol, "AllowAnonymousAttribute"))
        {
            return;
        }

        if (TypeOrInterfacesHaveScopeResource(classSymbol))
        {
            return;
        }

        if (HasScopeAttribute(classSymbol))
        {
            return;
        }

        foreach (var member in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(member);
            if (methodSymbol == null
                || !IsExposedApiMethod(methodSymbol))
            {
                continue;
            }

            if (!IsMutatingOperation(methodSymbol.Name))
            {
                continue;
            }

            if (HasAttribute(methodSymbol, "AllowAnonymousAttribute"))
            {
                continue;
            }

            if (HasScopeAttribute(methodSymbol))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                MutatingApiWithoutWriteScopeRule,
                member.Identifier.GetLocation(),
                methodSymbol.Name,
                classSymbol.Name));
        }
    }

    private static bool IsMutatingOperation(string methodName)
    {
        return !IsConfidentReadOperation(methodName);
    }

    private static bool IsConfidentReadOperation(string methodName)
    {
        if (string.IsNullOrEmpty(methodName))
        {
            return false;
        }

        var stripped = methodName.EndsWith("Async")
            ? methodName.Substring(0, methodName.Length - "Async".Length)
            : methodName;
        if (string.IsNullOrWhiteSpace(stripped))
        {
            return false;
        }

        var lowered = stripped.ToLowerInvariant();

        var matchedPrefix = string.Empty;
        foreach (var prefix in ReaderPrefixes)
        {
            if (lowered.StartsWith(prefix))
            {
                matchedPrefix = prefix;
                break;
            }
        }

        if (matchedPrefix.Length == 0)
        {
            return false;
        }

        return !RemainderShowsMutationSignal(stripped.Substring(matchedPrefix.Length));
    }

    private static bool RemainderShowsMutationSignal(string remainder)
    {
        foreach (var word in SplitRemainderWords(remainder))
        {
            foreach (var conjunction in ConjunctionTokens)
            {
                if (word == conjunction)
                {
                    return true;
                }
            }

            foreach (var token in MutatorTokens)
            {
                if (word.StartsWith(token))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<string> SplitRemainderWords(string remainder)
    {
        var words = new List<string>();
        var current = new List<char>();
        foreach (var character in remainder)
        {
            if (char.IsUpper(character)
                && current.Count > 0)
            {
                words.Add(new string(current.ToArray()).ToLowerInvariant());
                current = new List<char>();
            }

            current.Add(character);
        }

        if (current.Count > 0)
        {
            words.Add(new string(current.ToArray()).ToLowerInvariant());
        }

        return words;
    }

    private static bool TypeOrInterfacesHaveScopeResource(INamedTypeSymbol classSymbol)
    {
        if (HasAttribute(classSymbol, "ScopeResourceAttribute"))
        {
            return true;
        }

        foreach (var interfaceSymbol in classSymbol.AllInterfaces)
        {
            if (HasAttribute(interfaceSymbol, "ScopeResourceAttribute"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasScopeAttribute(ISymbol symbol)
    {
        return HasAttribute(symbol, "RequiresScopeAttribute")
            || HasAttribute(symbol, "RequiresScopeContractAttribute");
    }

    private static bool HasAttribute(ISymbol symbol,
                                     string attributeName)
    {
        return symbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == attributeName);
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

        return true;
    }
}
