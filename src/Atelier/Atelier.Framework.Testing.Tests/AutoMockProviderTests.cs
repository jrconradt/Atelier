using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;
using Xunit;

namespace Atelier.Framework.Testing.Tests;

public sealed class AutoMockProviderTests
{
    public interface IService
    {
        Task<Outcome<string>> DoAsync();

        int Compute();
    }

    public sealed class HasParameterlessCtor
    {
        public int Value { get; set; }
    }

    public sealed class DependsOnInterface
    {
        public DependsOnInterface(IService service)
        {
            Service = service;
        }

        public IService Service { get; }
    }

    public sealed class AlwaysThrowsInCtor
    {
        public AlwaysThrowsInCtor()
        {
            throw new InvalidOperationException("ctor blew up");
        }
    }

    public abstract class AbstractType
    {
    }

    public sealed class CycleA
    {
        public CycleA(CycleB b)
        {
            B = b;
        }

        public CycleB B { get; }
    }

    public sealed class CycleB
    {
        public CycleB(CycleA a)
        {
            A = a;
        }

        public CycleA A { get; }
    }

    public sealed class Depth1
    {
        public Depth1(Depth2 inner)
        {
            Inner = inner;
        }

        public Depth2 Inner { get; }
    }

    public sealed class Depth2
    {
        public Depth2(Depth3 inner)
        {
            Inner = inner;
        }

        public Depth3 Inner { get; }
    }

    public sealed class Depth3
    {
        public Depth3(Depth4 inner)
        {
            Inner = inner;
        }

        public Depth4 Inner { get; }
    }

    public sealed class Depth4
    {
        public Depth4(Depth5 inner)
        {
            Inner = inner;
        }

        public Depth5 Inner { get; }
    }

    public sealed class Depth5
    {
        public Depth5(Depth6 inner)
        {
            Inner = inner;
        }

        public Depth6 Inner { get; }
    }

    public sealed class Depth6
    {
        public Depth6(Depth7 inner)
        {
            Inner = inner;
        }

        public Depth7 Inner { get; }
    }

    public sealed class Depth7
    {
        public Depth7(Depth8 inner)
        {
            Inner = inner;
        }

        public Depth8 Inner { get; }
    }

    public sealed class Depth8
    {
        public Depth8(string deep)
        {
            Deep = deep;
        }

        public string Deep { get; }
    }

    [Fact]
    public void Value_ResolvesValueTypeToDefault()
    {
        Assert.Equal(0, AutoMockProvider.For<int>());
    }

    [Fact]
    public void Value_ResolvesStringToEmpty()
    {
        Assert.Equal(string.Empty, AutoMockProvider.For<string>());
    }

    [Fact]
    public void Value_ResolvesCancellationTokenToNone()
    {
        Assert.Equal(CancellationToken.None, AutoMockProvider.For<CancellationToken>());
    }

    [Fact]
    public void Value_ResolvesEmptyCollections()
    {
        var list = AutoMockProvider.For<List<int>>();
        Assert.NotNull(list);
        Assert.Empty(list!);

        var array = AutoMockProvider.For<string[]>();
        Assert.NotNull(array);
        Assert.Empty(array!);
    }

    [Fact]
    public async Task Value_ResolvesInterfaceToProxy_WithBenignOutcome()
    {
        var service = AutoMockProvider.For<IService>();

        Assert.NotNull(service);
        Assert.Equal(0, service!.Compute());

        var outcome = await service.DoAsync();
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void Value_ResolvesConcreteTypeThroughInterfaceDependency()
    {
        var instance = AutoMockProvider.For<DependsOnInterface>();

        Assert.NotNull(instance);
        Assert.NotNull(instance!.Service);
    }

    [Fact]
    public void Value_ResolvesParameterlessConcreteType()
    {
        var instance = AutoMockProvider.For<HasParameterlessCtor>();

        Assert.NotNull(instance);
    }

    [Fact]
    public void Threw_PropagatesConstructorExceptionForParameterlessType()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AutoMockProvider.For<AlwaysThrowsInCtor>());
        Assert.Equal("ctor blew up", ex.Message);
    }

    [Fact]
    public void NeedsFixture_ForAbstractClass()
    {
        var ex = Assert.Throws<NeedsFixtureException>(() => AutoMockProvider.For<AbstractType>());
        Assert.Contains("abstract", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NeedsFixture_ForOpenGenericType()
    {
        var ex = Assert.Throws<NeedsFixtureException>(() => AutoMockProvider.For(typeof(List<>)));
        Assert.Contains("open generic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NeedsFixture_WhenConcreteDependencyChainExceedsDepthCap()
    {
        var ex = Assert.Throws<NeedsFixtureException>(() => AutoMockProvider.For<Depth1>());
        Assert.Contains("no usable constructor", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at depth 4", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tried 0 ctors", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NeedsFixture_OnCyclicConstructors_WithoutStackOverflow()
    {
        var ex = Assert.Throws<NeedsFixtureException>(() => AutoMockProvider.For<CycleA>());
        Assert.Contains("no usable constructor", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at depth 4", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(CycleA), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(CycleB), ex.Message, StringComparison.Ordinal);
    }
}
