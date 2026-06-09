        [GeneratedTest("Operation/Cancellation-Honored", "{{ target }}")]
        public static {{ asyncKw }}Test_Op_Cancellation_{{ suffix }}()
        {
            {{ preamble }}
            var methodArgs = method.GetParameters().Select(mp => AutoMockProvider.For(mp.ParameterType)).ToArray();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            methodArgs[{{ ctIndex }}] = cts.Token;

            object? ret = null;
            try
            {
                ret = method.Invoke(instance, methodArgs);
                {{ awaitBlock }}
            }
            catch (TargetInvocationException tie) when (tie.InnerException is OperationCanceledException)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (TargetInvocationException tie) when (tie.InnerException is { } inner)
            {
                throw new InvalidOperationException($"Method threw {inner.GetType().Name} (expected Outcome.Failure or OperationCanceledException on cancelled token): {inner.Message}");
            }

            if (ret is null)
                throw new InvalidOperationException("Method returned null on cancelled token — expected Outcome.Failure or OperationCanceledException");
            var rt = ret.GetType();
            if (!IsAtelierOutcome(rt))
                throw new InvalidOperationException($"Method returned non-Outcome '{rt.FullName}' on cancelled token");
            var isSuccess = rt.GetProperty("IsSuccess")!.GetValue(ret) as bool?;
            if (isSuccess == true)
                throw new InvalidOperationException("Method returned Outcome success on pre-cancelled token — cancellation not honored");
        }
