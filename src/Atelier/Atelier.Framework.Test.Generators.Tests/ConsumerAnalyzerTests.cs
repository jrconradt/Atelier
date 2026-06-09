using Atelier.Framework.Test.Generators;
using Xunit;

namespace Atelier.Framework.Test.Generators.Tests;

public sealed class ConsumerAnalyzerTests
{
    [Fact]
    public void Analyze_CollectsRequisiteFields_AndDetectsGeneratedConstructor()
    {
        const string source = """
            using Atelier.Framework.Requisitions;

            namespace Sample;

            public interface IClock
            {
            }

            public partial class TimedService
            {
                [Requisite] private readonly IClock _clock = null!;
            }
            """;

        var compilation = CompilationFactory.Create(source);
        var symbol = CompilationFactory.GetType(compilation, "Sample.TimedService");

        var metadata = ConsumerAnalyzer.Analyze(symbol, compilation);

        Assert.NotNull(metadata);
        Assert.Single(metadata!.RequisiteFields);
        Assert.Equal("_clock", metadata.RequisiteFields[0].Name);
        Assert.True(metadata.RequisiteFields[0].TypeIsInterface);
        Assert.True(metadata.IsPartial);
        Assert.False(metadata.HasUserDeclaredConstructor);
        Assert.Equal(1, metadata.ExpectedCtorArity);
        Assert.True(metadata.GeneratorEmitsConstructor);
    }

    [Fact]
    public void Analyze_AddsLoggerArity_WhenImplementsIAtelier()
    {
        const string source = """
            using Atelier.Framework.Observability;
            using Atelier.Framework.Requisitions;

            namespace Sample;

            public interface IClock
            {
            }

            public partial class ObservedService : IAtelier
            {
                [Requisite] private readonly IClock _clock = null!;
            }
            """;

        var compilation = CompilationFactory.Create(source);
        var symbol = CompilationFactory.GetType(compilation, "Sample.ObservedService");

        var metadata = ConsumerAnalyzer.Analyze(symbol, compilation);

        Assert.NotNull(metadata);
        Assert.True(metadata!.ImplementsIAtelier);
        Assert.True(metadata.GeneratorAddsLogger);
        Assert.Equal(2, metadata.ExpectedCtorArity);
    }

    [Fact]
    public void Analyze_ReturnsNull_ForAbstractStaticAndGenericClasses()
    {
        const string source = """
            using Atelier.Framework.Requisitions;

            namespace Sample;

            public interface IClock
            {
            }

            public abstract class AbstractService
            {
                [Requisite] private readonly IClock _clock = null!;
            }

            public static class StaticService
            {
            }

            public sealed class GenericService<T>
            {
                [Requisite] private readonly IClock _clock = null!;
            }
            """;

        var compilation = CompilationFactory.Create(source);

        Assert.Null(ConsumerAnalyzer.Analyze(CompilationFactory.GetType(compilation, "Sample.AbstractService"), compilation));
        Assert.Null(ConsumerAnalyzer.Analyze(CompilationFactory.GetType(compilation, "Sample.StaticService"), compilation));
        Assert.Null(ConsumerAnalyzer.Analyze(CompilationFactory.GetType(compilation, "Sample.GenericService`1"), compilation));
    }

    [Fact]
    public void Analyze_DetectsOperationMethod_AndOutcomeShape()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Outcomes;

            namespace Sample;

            public sealed class GreeterService
            {
                [Operation("greet")]
                public Task<Outcome<string>> GreetAsync(string name, CancellationToken cancellationToken)
                    => Task.FromResult(Outcome<string>.Success(name));
            }
            """;

        var compilation = CompilationFactory.Create(source);
        var symbol = CompilationFactory.GetType(compilation, "Sample.GreeterService");

        var metadata = ConsumerAnalyzer.Analyze(symbol, compilation);

        Assert.NotNull(metadata);
        var operation = Assert.Single(metadata!.Operations);
        Assert.Equal("GreetAsync", operation.Name);
        Assert.Equal("greet", operation.OperationName);
        Assert.True(operation.IsAsync);
        Assert.True(operation.ReturnsOutcomeShape);
        Assert.Equal(2, operation.Parameters.Count);
        Assert.True(operation.Parameters[0].IsString);
        Assert.True(operation.Parameters[1].IsCancellationToken);
    }

    [Fact]
    public void ImplementsIAtelier_OnlyMatchesFullyQualifiedInterface()
    {
        const string source = """
            using Atelier.Framework.Observability;

            namespace Sample;

            public interface IAtelier
            {
            }

            public sealed class LookalikeService : IAtelier
            {
            }

            public sealed class RealService : Atelier.Framework.Observability.IAtelier
            {
                public void Observe(
                    LogLevel level = LogLevel.Information,
                    System.Exception? exception = null,
                    string? message = null,
                    params System.ReadOnlySpan<(string Key, object Value)> values)
                {
                }
            }
            """;

        var compilation = CompilationFactory.Create(source);

        Assert.False(ConsumerAnalyzer.ImplementsIAtelier(CompilationFactory.GetType(compilation, "Sample.LookalikeService")));
        Assert.True(ConsumerAnalyzer.ImplementsIAtelier(CompilationFactory.GetType(compilation, "Sample.RealService")));
    }

    [Fact]
    public void IsOutcomeShape_RecognizesOutcomeAcrossTaskWrappers()
    {
        const string source = """
            using System.Threading.Tasks;
            using Atelier.Framework.Outcomes;

            namespace Sample;

            public sealed class Shapes
            {
                public Outcome Plain() => Outcome.Success();
                public Outcome<string> Generic() => Outcome<string>.Success(string.Empty);
                public Task<Outcome<int>> AsyncGeneric() => Task.FromResult(Outcome<int>.Success(0));
                public ValueTask<Outcome> AsyncPlain() => new(Outcome.Success());
                public Task<int> NotOutcome() => Task.FromResult(0);
            }
            """;

        var compilation = CompilationFactory.Create(source);
        var shapes = CompilationFactory.GetType(compilation, "Sample.Shapes");

        Microsoft.CodeAnalysis.ITypeSymbol ReturnTypeOf(string method) =>
            ((Microsoft.CodeAnalysis.IMethodSymbol)shapes.GetMembers(method).Single()).ReturnType;

        Assert.True(ConsumerAnalyzer.IsOutcomeShape(ReturnTypeOf("Plain")));
        Assert.True(ConsumerAnalyzer.IsOutcomeShape(ReturnTypeOf("Generic")));
        Assert.True(ConsumerAnalyzer.IsOutcomeShape(ReturnTypeOf("AsyncGeneric")));
        Assert.True(ConsumerAnalyzer.IsOutcomeShape(ReturnTypeOf("AsyncPlain")));
        Assert.False(ConsumerAnalyzer.IsOutcomeShape(ReturnTypeOf("NotOutcome")));
    }
}
