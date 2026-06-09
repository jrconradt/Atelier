        [GeneratedTest("Operation/No-Throw-On-Default-Input", "{{ target }}")]
        public static {{ asyncKw }}Test_Op_NoThrow_{{ suffix }}()
        {
            {{ preamble }}
            var methodArgs = method.GetParameters().Select(mp => AutoMockProvider.For(mp.ParameterType)).ToArray();

            object? ret = null;
            try
            {
                ret = method.Invoke(instance, methodArgs);
                {{ awaitBlock }}
            }
            catch (TargetInvocationException tie) when (tie.InnerException is { } inner)
            {
                throw new InvalidOperationException($"Method threw {inner.GetType().Name}: {inner.Message}");
            }

            {{ outcomeCheck }}
        }
