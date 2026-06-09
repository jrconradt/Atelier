        [GeneratedTest("Operation/Empty-String-Tolerated", "{{ target }}")]
        public static {{ asyncKw }}Test_Op_EmptyString_{{ suffix }}_{{ paramIdent }}()
        {
            {{ preamble }}
            var methodArgs = method.GetParameters().Select(mp => AutoMockProvider.For(mp.ParameterType)).ToArray();
            methodArgs[{{ paramIndex }}] = string.Empty;

            object? ret = null;
            try
            {
                ret = method.Invoke(instance, methodArgs);
                {{ awaitBlock }}
            }
            catch (TargetInvocationException tie) when (tie.InnerException is { } inner)
            {
                throw new InvalidOperationException($"Method threw {inner.GetType().Name} on empty-string '{{ paramName }}': {inner.Message}");
            }

            {{ outcomeCheck }}
        }
