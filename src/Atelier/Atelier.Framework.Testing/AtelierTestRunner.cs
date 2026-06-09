using System.Reflection;

namespace Atelier.Framework.Testing;

public static class AtelierTestRunner
{
    public static async Task<TestReport> RunAsync(
        IEnumerable<Assembly> assemblies,
        bool dryRun = false,
        Action<TestResult>? onResult = null,
        string? filter = null)
    {
        var results = new List<TestResult>();

        foreach (var asm in assemblies)
        {
            foreach (var (type, method, attr) in EnumerateTests(asm))
            {
                var testId = $"{type.FullName}.{method.Name}";
                if (filter is { Length: > 0 } && !testId.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !attr.Invariant.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TestResult result;
                if (dryRun)
                {
                    result = new TestResult(
                        asm.GetName().Name ?? "?",
                        type.FullName ?? type.Name,
                        method.Name,
                        attr.Invariant,
                        attr.Target,
                        TestStatus.Pass,
                        Detail: "(dry-run)");
                }
                else
                {
                    result = await RunOneAsync(asm, type, method, attr).ConfigureAwait(false);
                }

                results.Add(result);
                onResult?.Invoke(result);
            }
        }

        return BuildReport(results);
    }

    private static IEnumerable<(Type, MethodInfo, GeneratedTestAttribute)> EnumerateTests(Assembly asm)
    {
        Type[] types;
        try
        {
            types = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.OfType<Type>().ToArray();
        }

        var discovered = new List<(Type Type, MethodInfo Method, GeneratedTestAttribute Attr)>();

        foreach (var type in types)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[runner] skipped type '{type.FullName ?? type.Name}' in '{asm.GetName().Name}': {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<GeneratedTestAttribute>();
                if (attr is null)
                {
                    continue;
                }
                discovered.Add((type, method, attr));
            }
        }

        return discovered
            .OrderBy(t => t.Type.FullName ?? t.Type.Name, StringComparer.Ordinal)
            .ThenBy(t => t.Method.Name, StringComparer.Ordinal)
            .Select(t => (t.Type, t.Method, t.Attr));
    }

    private static async Task<TestResult> RunOneAsync(
        Assembly asm,
        Type type,
        MethodInfo method,
        GeneratedTestAttribute attr)
    {
        var name = asm.GetName().Name ?? "?";
        var typeName = type.FullName ?? type.Name;

        try
        {
            var ret = method.IsStatic
                ? method.Invoke(null, null)
                : method.Invoke(Activator.CreateInstance(type), null);

            if (ret is Task t)
            {
                await t.ConfigureAwait(false);
            }
            else if (ret is ValueTask vt)
            {
                await vt.ConfigureAwait(false);
            }
            else if (ret is not null
                && ret.GetType() is { IsGenericType: true } retType
                && retType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                var asTask = retType.GetMethod(nameof(ValueTask<object>.AsTask), Type.EmptyTypes);
                if (asTask?.Invoke(ret, null) is Task valueTaskAsTask)
                {
                    await valueTaskAsTask.ConfigureAwait(false);
                }
            }

            return new TestResult(name, typeName, method.Name, attr.Invariant, attr.Target, TestStatus.Pass);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is NeedsFixtureException nfe)
        {
            return new TestResult(name, typeName, method.Name, attr.Invariant, attr.Target,
                TestStatus.NeedsFixture, nfe.Message, nameof(NeedsFixtureException));
        }
        catch (NeedsFixtureException nfe)
        {
            return new TestResult(name, typeName, method.Name, attr.Invariant, attr.Target,
                TestStatus.NeedsFixture, nfe.Message, nameof(NeedsFixtureException));
        }
        catch (TargetInvocationException tie)
        {
            var inner = tie.InnerException ?? tie;
            return new TestResult(name, typeName, method.Name, attr.Invariant, attr.Target,
                TestStatus.Fail, inner.Message, inner.GetType().Name);
        }
        catch (Exception ex)
        {
            return new TestResult(name, typeName, method.Name, attr.Invariant, attr.Target,
                TestStatus.Fail, ex.Message, ex.GetType().Name);
        }
    }

    private static TestReport BuildReport(IReadOnlyList<TestResult> results)
    {
        int pass = 0, fail = 0, nf = 0;
        foreach (var r in results)
        {
            switch (r.Status)
            {
                case TestStatus.Pass:
                    pass++;
                    break;
                case TestStatus.Fail:
                    fail++;
                    break;
                case TestStatus.NeedsFixture:
                    nf++;
                    break;
            }
        }
        return new TestReport(results.Count, pass, fail, nf, results);
    }
}
