        [GeneratedTest("Operation/Null-Param-Honored", "{{ target }}")]
        public static {{ asyncKw }}Test_Op_NullParam_{{ suffix }}_{{ paramIdent }}()
        {
            {{ preamble }}
            var methodArgs = method.GetParameters().Select(mp => AutoMockProvider.For(mp.ParameterType)).ToArray();
            methodArgs[{{ paramIndex }}] = null;

            object? ret = null;
            try
            {
                ret = method.Invoke(instance, methodArgs);
                {{ awaitBlock }}
            }
            catch (TargetInvocationException tie) when (tie.InnerException is NullReferenceException nre)
            {
                throw new InvalidOperationException("Method threw NullReferenceException on null '{{ paramName }}' — missing null-check (expected Outcome.Failure)");
            }
            catch (TargetInvocationException tie) when (tie.InnerException is ArgumentNullException ane)
            {
                throw new InvalidOperationException("Method threw ArgumentNullException on null '{{ paramName }}' — should return Outcome.Failure instead");
            }
            catch (TargetInvocationException tie) when (tie.InnerException is { } inner)
            {
                throw new InvalidOperationException($"Method threw {inner.GetType().Name}: {inner.Message}");
            }

            if (ret is null)
                throw new InvalidOperationException("Method returned null on null '{{ paramName }}' — expected Outcome.Failure");
            var rt = ret.GetType();
            if (!IsAtelierOutcome(rt))
                throw new InvalidOperationException($"Method returned non-Outcome '{rt.FullName}'");
            var isSuccess = rt.GetProperty("IsSuccess")!.GetValue(ret) as bool?;
            if (isSuccess == true)
                throw new InvalidOperationException("Method returned Outcome success on null '{{ paramName }}' — null-check missing");
        }
