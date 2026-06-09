using System.Reflection;
using System.Runtime.ExceptionServices;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Testing;

public static class AutoMockProvider
{
    private const int MAX_CONCRETE_DEPTH = 4;

    internal const int MAX_PROXY_DEPTH = 4;

    public static T For<T>() => (T)For(typeof(T))!;

    public static object? For(Type type) => For(type, proxyDepth: 0);

    internal static object? For(Type type, int proxyDepth)
    {
        var result = Resolve(type, depth: 0, proxyDepth);
        if (result.Kind == ResolutionKind.Value)
        {
            return result.Value;
        }
        if (result.Kind == ResolutionKind.NeedsFixture)
        {
            throw new NeedsFixtureException(result.Message!);
        }
        ExceptionDispatchInfo.Capture(result.Exception!).Throw();
        return null;
    }

    private static Resolution Resolve(Type rootType, int depth, int proxyDepth)
    {
        var stack = new Stack<Frame>();
        stack.Push(new Frame(rootType, depth));
        Resolution lastResult = default;

        while (stack.Count > 0)
        {
            var frame = stack.Peek();

            if (!frame.Entered)
            {
                frame.Entered = true;
                if (TryShortCircuit(frame, out var shortResult, out var beginCtorLoop, proxyDepth))
                {
                    frame.Result = shortResult;
                    frame.Done = true;
                }
                else if (beginCtorLoop)
                {
                    PrepareCtorLoop(frame);
                }
                else
                {
                    frame.Result = shortResult;
                    frame.Done = true;
                }
            }
            else if (frame.AwaitingChild)
            {
                frame.AwaitingChild = false;
                ConsumeChild(frame, lastResult);
            }

            if (frame.Done)
            {
                stack.Pop();
                lastResult = frame.Result;
                continue;
            }

            if (TryAdvanceCtorLoop(frame, out var childFrame))
            {
                if (childFrame is not null)
                {
                    frame.AwaitingChild = true;
                    stack.Push(childFrame);
                }
            }
        }

        return lastResult;
    }

    private static bool TryShortCircuit(
        Frame frame,
        out Resolution result,
        out bool beginCtorLoop,
        int proxyDepth)
    {
        beginCtorLoop = false;
        var type = frame.Type;

        if (type == typeof(CancellationToken))
        {
            result = Resolution.FromValue(CancellationToken.None);
            return true;
        }

        if (type.ContainsGenericParameters)
        {
            result = Resolution.NeedsFixtureFor(
                $"Cannot auto-mock open generic type '{type.FullName ?? type.Name}' " +
                $"(contains unbound type parameters). Generic operations need " +
                $"per-instantiation tests; provide a fixture for a closed " +
                $"construction if you need to cover this path.");
            return true;
        }

        if (type.IsValueType)
        {
            try
            {
                result = Resolution.FromValue(Activator.CreateInstance(type));
            }
            catch (Exception ex)
            {
                result = Resolution.FromException(ex);
            }
            return true;
        }

        if (type == typeof(string))
        {
            result = Resolution.FromValue(string.Empty);
            return true;
        }

        object? fromFixture;
        try
        {
            fromFixture = TestFixtures.TryCreate(type);
        }
        catch (Exception ex)
        {
            result = Resolution.FromException(ex);
            return true;
        }
        if (fromFixture is not null)
        {
            result = Resolution.FromValue(fromFixture);
            return true;
        }

        if (TryEmptyCollection(type, out var collection))
        {
            result = Resolution.FromValue(collection);
            return true;
        }

        if (type.IsInterface)
        {
            try
            {
                result = Resolution.FromValue(CreateInterfaceProxy(type, proxyDepth));
            }
            catch (Exception ex)
            {
                result = Resolution.FromException(ex);
            }
            return true;
        }

        if (typeof(MulticastDelegate).IsAssignableFrom(type) && type != typeof(MulticastDelegate))
        {
            try
            {
                result = Resolution.FromValue(CreateDelegateNoOp(type));
            }
            catch (Exception ex)
            {
                result = Resolution.FromException(ex);
            }
            return true;
        }

        if (type.IsAbstract)
        {
            result = Resolution.NeedsFixtureFor(
                $"Cannot auto-mock abstract class '{type.FullName}'. " +
                $"Provide a fixture via TestFixtures.Register<{type.Name}>(...) or change the [Requisite] field to an interface.");
            return true;
        }

        var parameterlessCtor = type.GetConstructor(
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (parameterlessCtor is not null)
        {
            try
            {
                result = Resolution.FromValue(parameterlessCtor.Invoke(null));
                return true;
            }
            catch (TargetInvocationException tie)
            {
                result = Resolution.FromException(tie.InnerException ?? tie);
                return true;
            }
            catch (Exception ex)
            {
                result = Resolution.FromException(ex);
                return true;
            }
        }

        result = default;
        beginCtorLoop = true;
        return false;
    }

    private static void PrepareCtorLoop(Frame frame)
    {
        if (frame.Depth < MAX_CONCRETE_DEPTH)
        {
            frame.Ctors = frame.Type
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .ToArray();
        }
        else
        {
            frame.Ctors = Array.Empty<ConstructorInfo>();
        }
        frame.CtorCount = frame.Ctors.Length;
        frame.CtorIndex = -1;
    }

    private static bool TryAdvanceCtorLoop(Frame frame, out Frame? childFrame)
    {
        childFrame = null;

        while (true)
        {
            if (frame.CurrentCtor is null)
            {
                frame.CtorIndex++;
                if (frame.CtorIndex >= frame.Ctors!.Length)
                {
                    frame.Result = Resolution.NeedsFixtureFor(
                        $"Cannot auto-mock concrete type '{frame.Type.FullName}' — no usable constructor " +
                        $"(tried {frame.CtorCount} ctors at depth {frame.Depth}, last failure: {frame.LastFailure ?? "none"}). " +
                        $"Provide a fixture via TestFixtures.Register<{frame.Type.Name}>(...) or change the [Requisite] field to an interface.");
                    frame.Done = true;
                    return false;
                }

                frame.CurrentCtor = frame.Ctors[frame.CtorIndex];
                frame.CurrentParams = frame.CurrentCtor.GetParameters();
                frame.CurrentArgs = new object?[frame.CurrentParams.Length];
                frame.ParamIndex = 0;
            }

            if (frame.ParamIndex < frame.CurrentParams!.Length)
            {
                var p = frame.CurrentParams[frame.ParamIndex];
                childFrame = new Frame(p.ParameterType, frame.Depth + 1);
                return true;
            }

            try
            {
                frame.Result = Resolution.FromValue(frame.CurrentCtor.Invoke(frame.CurrentArgs));
                frame.Done = true;
                return false;
            }
            catch (TargetInvocationException tie)
            {
                frame.LastFailure = $"ctor threw {tie.InnerException?.GetType().Name}: {tie.InnerException?.Message}";
            }
            catch (Exception ex)
            {
                frame.LastFailure = $"ctor threw {ex.GetType().Name}: {ex.Message}";
            }

            frame.CurrentCtor = null;
        }
    }

    private static void ConsumeChild(Frame frame, Resolution childResult)
    {
        var p = frame.CurrentParams![frame.ParamIndex];

        if (childResult.Kind == ResolutionKind.Value)
        {
            frame.CurrentArgs![frame.ParamIndex] = childResult.Value;
            frame.ParamIndex++;
            return;
        }

        if (childResult.Kind == ResolutionKind.NeedsFixture)
        {
            frame.LastFailure = $"param '{p.Name}': {childResult.Message}";
        }
        else
        {
            var ex = childResult.Exception!;
            frame.LastFailure = $"param '{p.Name}': {ex.GetType().Name} — {ex.Message}";
        }

        frame.CurrentCtor = null;
    }

    private static object CreateInterfaceProxy(Type interfaceType, int proxyDepth)
    {
        var createMethod = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == nameof(DispatchProxy.Create) &&
                m.IsGenericMethodDefinition &&
                m.GetGenericArguments().Length == 2 &&
                m.GetParameters().Length == 0)
            ?? throw new InvalidOperationException("DispatchProxy.Create<T, TProxy>() not found");
        var generic = createMethod.MakeGenericMethod(interfaceType, typeof(NoOpDispatchProxy));
        var proxy = generic.Invoke(null, null)
            ?? throw new InvalidOperationException($"DispatchProxy.Create returned null for {interfaceType}");
        ((NoOpDispatchProxy)proxy).Depth = proxyDepth;
        return proxy;
    }

    private static object CreateDelegateNoOp(Type delegateType)
    {
        var invoke = delegateType.GetMethod("Invoke")
            ?? throw new InvalidOperationException($"Delegate type '{delegateType}' has no Invoke method");

        var parameters = invoke.GetParameters()
            .Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType, p.Name))
            .ToArray();

        System.Linq.Expressions.Expression body;
        var returnType = invoke.ReturnType;

        if (returnType == typeof(void))
        {
            body = System.Linq.Expressions.Expression.Empty();
        }
        else if (returnType == typeof(Task))
        {
            var completedTask = typeof(Task).GetProperty(nameof(Task.CompletedTask))!;
            body = System.Linq.Expressions.Expression.Property(null, completedTask);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var t = returnType.GetGenericArguments()[0];
            var fromResult = typeof(Task).GetMethods()
                .First(m => m.Name == nameof(Task.FromResult) && m.IsGenericMethodDefinition)
                .MakeGenericMethod(t);
            body = System.Linq.Expressions.Expression.Call(fromResult,
                System.Linq.Expressions.Expression.Default(t));
        }
        else
        {
            body = System.Linq.Expressions.Expression.Default(returnType);
        }

        return System.Linq.Expressions.Expression.Lambda(delegateType, body, parameters).Compile();
    }

    internal static bool TryEmptyCollection(Type type, out object? value)
    {
        value = null;

        if (type.IsArray && type.GetArrayRank() == 1)
        {
            value = Array.CreateInstance(type.GetElementType()!, 0);
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var def = type.GetGenericTypeDefinition();
        var args = type.GetGenericArguments();

        if (def == typeof(List<>)
            || def == typeof(IList<>)
            || def == typeof(ICollection<>)
            || def == typeof(IEnumerable<>)
            || def == typeof(IReadOnlyList<>)
            || def == typeof(IReadOnlyCollection<>))
        {
            value = Activator.CreateInstance(typeof(List<>).MakeGenericType(args[0]));
            return value is not null;
        }

        if (def == typeof(Dictionary<,>)
            || def == typeof(IDictionary<,>)
            || def == typeof(IReadOnlyDictionary<,>))
        {
            value = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args[0], args[1]));
            return value is not null;
        }

        return false;
    }

    private enum ResolutionKind
    {
        Value,
        NeedsFixture,
        Threw
    }

    private readonly struct Resolution
    {
        public ResolutionKind Kind { get; }
        public object? Value { get; }
        public string? Message { get; }
        public Exception? Exception { get; }

        private Resolution(
            ResolutionKind kind,
            object? value,
            string? message,
            Exception? exception)
        {
            Kind = kind;
            Value = value;
            Message = message;
            Exception = exception;
        }

        public static Resolution FromValue(object? value)
            => new Resolution(ResolutionKind.Value, value, null, null);

        public static Resolution NeedsFixtureFor(string message)
            => new Resolution(ResolutionKind.NeedsFixture, null, message, null);

        public static Resolution FromException(Exception exception)
            => new Resolution(ResolutionKind.Threw, null, null, exception);
    }

    private sealed class Frame
    {
        public Frame(Type type, int depth)
        {
            Type = type;
            Depth = depth;
        }

        public Type Type { get; }
        public int Depth { get; }
        public bool Entered { get; set; }
        public bool Done { get; set; }
        public bool AwaitingChild { get; set; }
        public Resolution Result { get; set; }

        public ConstructorInfo[]? Ctors { get; set; }
        public int CtorCount { get; set; }
        public int CtorIndex { get; set; }
        public ConstructorInfo? CurrentCtor { get; set; }
        public ParameterInfo[]? CurrentParams { get; set; }
        public object?[]? CurrentArgs { get; set; }
        public int ParamIndex { get; set; }
        public string? LastFailure { get; set; }
    }
}

public class NoOpDispatchProxy : DispatchProxy
{
    internal int Depth { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            return null;
        }
        return DefaultFor(targetMethod.ReturnType, Depth);
    }

    private static object? DefaultFor(Type type, int depth)
    {
        var wrappers = new Stack<Type>();
        var current = type;

        while (current.IsGenericType)
        {
            var def = current.GetGenericTypeDefinition();
            if (def == typeof(Task<>) || def == typeof(ValueTask<>))
            {
                wrappers.Push(def);
                current = current.GetGenericArguments()[0];
                continue;
            }
            break;
        }

        object? value = BaseDefault(current, depth);

        while (wrappers.Count > 0)
        {
            var def = wrappers.Pop();
            if (def == typeof(Task<>))
            {
                var fromResult = typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(current);
                value = fromResult.Invoke(null, new[] { value });
            }
            else
            {
                value = Activator.CreateInstance(def.MakeGenericType(current), value);
            }
            current = def.MakeGenericType(current);
        }

        return value;
    }

    private static object? BaseDefault(Type t, int depth)
    {
        if (t == typeof(void))
        {
            return null;
        }
        if (t == typeof(Task))
        {
            return Task.CompletedTask;
        }
        if (t == typeof(ValueTask))
        {
            return new ValueTask();
        }

        if (TryBenignOutcome(t, depth, out var benign))
        {
            return benign;
        }

        if (typeof(Type).IsAssignableFrom(t))
        {
            return typeof(object);
        }

        if (t == typeof(string))
        {
            return string.Empty;
        }

        if (AutoMockProvider.TryEmptyCollection(t, out var collection))
        {
            return collection;
        }

        if (t.IsInterface || (!t.IsValueType && t != typeof(string)))
        {
            if (depth >= AutoMockProvider.MAX_PROXY_DEPTH)
            {
                return null;
            }
            try
            {
                return AutoMockProvider.For(t, depth + 1);
            }
            catch
            {
                return null;
            }
        }

        return ScalarDefault(t);
    }

    private static bool TryBenignOutcome(Type t, int depth, out object? outcome)
    {
        outcome = null;

        if (t == typeof(Outcome))
        {
            outcome = Outcome.Success();
            return true;
        }

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Outcome<>))
        {
            var inner = t.GetGenericArguments()[0];
            var success = t.GetMethod(
                nameof(Outcome<object>.Success),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { inner },
                modifiers: null);
            if (success is null)
            {
                return false;
            }

            object? defaultData;
            try
            {
                if (inner.IsValueType)
                {
                    defaultData = Activator.CreateInstance(inner);
                }
                else if (depth >= AutoMockProvider.MAX_PROXY_DEPTH)
                {
                    return false;
                }
                else
                {
                    defaultData = AutoMockProvider.For(inner, depth + 1);
                }
            }
            catch
            {
                return false;
            }

            if (defaultData is null)
            {
                return false;
            }

            outcome = success.Invoke(null, new[] { defaultData });
            return outcome is not null;
        }

        return false;
    }

    private static object? ScalarDefault(Type t)
        => t.IsValueType ? Activator.CreateInstance(t) : null;
}
