using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Atelier.Framework.Analyzers.Tests;

internal static class NullableContext
{
    public static Solution Enable(Solution solution,
                                  ProjectId projectId)
    {
        var project = solution.GetProject(projectId)!;
        var options = (CSharpCompilationOptions)project.CompilationOptions!;
        return solution.WithProjectCompilationOptions(
            projectId,
            options.WithNullableContextOptions(NullableContextOptions.Enable));
    }
}

internal static class FrameworkReferences
{
    public static IEnumerable<MetadataReference> All()
    {
        foreach (var reference in RuntimeReferences())
        {
            yield return reference;
        }
        yield return MetadataReference.CreateFromFile(typeof(InfrastructureAttribute).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(OperationAttribute).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(RequisiteAttribute).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Outcome).Assembly.Location);
    }

    private static IEnumerable<MetadataReference> RuntimeReferences()
    {
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trusted is null)
        {
            yield break;
        }
        foreach (var path in trusted.Split(Path.PathSeparator))
        {
            if (path.Length > 0
                && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }
    }
}

internal sealed class AtelierAnalyzerTest<TAnalyzer> : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public AtelierAnalyzerTest()
    {
        ReferenceAssemblies = new ReferenceAssemblies("net10.0");
        foreach (var reference in FrameworkReferences.All())
        {
            TestState.AdditionalReferences.Add(reference);
        }
        SolutionTransforms.Add(NullableContext.Enable);
    }
}

internal sealed class AtelierCodeFixTest<TAnalyzer, TCodeFix> : CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public AtelierCodeFixTest()
    {
        ReferenceAssemblies = new ReferenceAssemblies("net10.0");
        foreach (var reference in FrameworkReferences.All())
        {
            TestState.AdditionalReferences.Add(reference);
        }
        SolutionTransforms.Add(NullableContext.Enable);
    }
}

internal static class AnalyzerVerify
{
    public static async Task FiresAsync<TAnalyzer>(
        string source,
        params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new AtelierAnalyzerTest<TAnalyzer>
        {
            TestCode = source,
        };
        foreach (var diagnostic in expected)
        {
            test.ExpectedDiagnostics.Add(diagnostic);
        }
        await test.RunAsync();
    }

    public static async Task SilentAsync<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new AtelierAnalyzerTest<TAnalyzer>
        {
            TestCode = source,
        };
        await test.RunAsync();
    }

    public static async Task CodeFixAsync<TAnalyzer, TCodeFix>(
        string before,
        string after,
        params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var test = new AtelierCodeFixTest<TAnalyzer, TCodeFix>
        {
            TestCode = before,
            FixedCode = after,
        };
        foreach (var diagnostic in expected)
        {
            test.ExpectedDiagnostics.Add(diagnostic);
        }
        await test.RunAsync();
    }
}
