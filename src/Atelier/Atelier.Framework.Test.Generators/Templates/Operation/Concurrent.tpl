        [GeneratedTest("Operation/Concurrent-Invocation-Safe", "{{ target }}")]
        public static async Task Test_Op_Concurrent_{{ suffix }}()
        {
            {{ preamble }}
            MethodInfo resolvedMethod = method;

            static async Task<bool?> InvokeOnceAsync(MethodInfo m, Func<object> makeReceiver)
            {
                var target = makeReceiver();
                var args = m.GetParameters().Select(mp => AutoMockProvider.For(mp.ParameterType)).ToArray();
                object? ret = m.Invoke(target, args);
                if (ret is Task t)
                {
                    await t.ConfigureAwait(false);
                    ret = ReadTaskResult(t);
                }
                else if (ret is ValueTask vt)
                {
                    await vt.ConfigureAwait(false);
                    ret = null;
                }
                else if (ret is not null)
                {
                    var asTaskMi = ret.GetType().GetMethod("AsTask");
                    if (asTaskMi is not null)
                    {
                        var taskObj = asTaskMi.Invoke(ret, null) as Task;
                        if (taskObj is not null)
                        {
                            await taskObj.ConfigureAwait(false);
                            ret = ReadTaskResult(taskObj);
                        }
                    }
                }
                if (ret is null)
                {
                    return null;
                }
                var rt = ret.GetType();
                if (!IsAtelierOutcome(rt))
                {
                    return null;
                }
                return rt.GetProperty("IsSuccess")!.GetValue(ret) as bool?;
            }

            bool? baseline;
            try
            {
                baseline = await InvokeOnceAsync(resolvedMethod, newReceiver).ConfigureAwait(false);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is { } inner)
            {
                throw new InvalidOperationException("Single-threaded baseline invocation threw " + inner.GetType().Name + ": " + inner.Message);
            }

            const int N = 8;
            var tasks = new Task<bool?>[N];
            for (int i = 0; i < N; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        return await InvokeOnceAsync(resolvedMethod, newReceiver).ConfigureAwait(false);
                    }
                    catch (TargetInvocationException tie) when (tie.InnerException is { } inner)
                    {
                        throw new InvalidOperationException("Concurrent invocation threw " + inner.GetType().Name + ": " + inner.Message);
                    }
                });
            }

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] != baseline)
                {
                    throw new InvalidOperationException("Concurrent invocation " + i + " produced IsSuccess=" + results[i] + " but single-threaded baseline was " + baseline + " — shared-state corruption under concurrency");
                }
            }
        }
