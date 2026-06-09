using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AmbiguousOperationNameAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER0751";

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
    };

    private static readonly string[] ConjunctionTokens = new[]
    {
        "or",
        "and",
        "then",
    };

    private static readonly DiagnosticDescriptor AmbiguousOperationNameRule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Operation name is lexically ambiguous between read and mutation",
        "Operation '{0}' on [Api] class '{1}' starts with a reader prefix but compounds with a mutating clause, so its read/write tier cannot be classified. Rename it to an unambiguous verb (a pure reader or a pure mutator) so the authorization scope tier is derivable.",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An exposed [Api] operation whose name begins with a reader prefix (get/fetch/retrieve/discover/find/list/query/search) yet joins a mutating clause via a conjunction (or/and/then) or embeds a mutator token (create/delete/update/...) cannot be tier-classified: the name reads like a query but performs a mutation. The scope-tier derivation would lexically classify it as a reader and admit a read-only principal to a state change. The name is refused at compile time; rename to a single unambiguous verb.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AmbiguousOperationNameRule);

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

        foreach (var member in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(member);
            if (methodSymbol == null
                || !IsExposedApiMethod(methodSymbol))
            {
                continue;
            }

            if (!IsAmbiguousOperationName(methodSymbol.Name))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                AmbiguousOperationNameRule,
                member.Identifier.GetLocation(),
                methodSymbol.Name,
                classSymbol.Name));
        }
    }

    private static bool IsAmbiguousOperationName(string methodName)
    {
        var stripped = methodName.EndsWith("Async")
            ? methodName.Substring(0, methodName.Length - "Async".Length)
            : methodName;
        var lowered = stripped.ToLowerInvariant();

        var matchedPrefix = MatchedReaderPrefix(lowered);
        if (matchedPrefix == null)
        {
            return false;
        }

        var remainder = lowered.Substring(matchedPrefix.Length);
        if (remainder.Length == 0)
        {
            return false;
        }

        foreach (var conjunction in ConjunctionTokens)
        {
            if (remainder.StartsWith(conjunction))
            {
                return true;
            }
        }

        foreach (var token in MutatorTokens)
        {
            if (remainder.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    private static string? MatchedReaderPrefix(string lowered)
    {
        foreach (var prefix in ReaderPrefixes)
        {
            if (lowered.StartsWith(prefix))
            {
                return prefix;
            }
        }

        return null;
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
