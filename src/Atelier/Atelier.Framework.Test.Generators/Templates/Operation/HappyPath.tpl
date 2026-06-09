        [GeneratedTest("Operation/Happy-Path-Success", "{{ target }}")]
        public static {{ asyncKw }}Test_Op_HappyPath_{{ suffix }}()
        {
            {{ preamble }}
            var methodArgs = method.GetParameters().Select(mp => AutoMockProvider.For(mp.ParameterType)).ToArray();
            {{ argOverrides }}

            object? ret = null;
            try
            {
                ret = method.Invoke(instance, methodArgs);
                {{ awaitBlock }}
            }
            catch (NeedsFixtureException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new NeedsFixtureException("Operation cancelled on a pre-cancelled token — likely long-running or streaming; register a TestFixtures fixture supplying terminating inputs.");
            }
            catch (TargetInvocationException tie) when (tie.InnerException is OperationCanceledException)
            {
                throw new NeedsFixtureException("Operation cancelled on a pre-cancelled token — likely long-running or streaming; register a TestFixtures fixture supplying terminating inputs.");
            }
            catch (TargetInvocationException tie) when (tie.InnerException is { } inner)
            {
                throw new InvalidOperationException($"Method threw {inner.GetType().Name} on valid input: {inner.Message}");
            }

            if (ret is null)
            {
                throw new InvalidOperationException("Operation returned null on valid input — expected Outcome.Success");
            }
            var rt = ret.GetType();
            if (!IsAtelierOutcome(rt))
            {
                throw new InvalidOperationException($"Return value '{rt.FullName}' is not an Atelier Outcome (expected Atelier.Framework.Outcomes.Outcome or Outcome<T>)");
            }
            var isSuccess = rt.GetProperty("IsSuccess")!.GetValue(ret) as bool?;
            if (isSuccess != true)
            {
                throw new InvalidOperationException("Operation returned Outcome.Failure on valid input — the operation is wired to fail on generator-supplied valid input; fix the operation or register a TestFixtures fixture supplying domain-valid inputs.");
            }
            if (rt.IsGenericType && rt.GetGenericTypeDefinition().FullName == "Atelier.Framework.Outcomes.Outcome`1")
            {
                var dataType = rt.GetGenericArguments()[0];
                if (!dataType.IsValueType
                    && dataType != typeof(string)
                    && TestFixtures.RegisteredTypes.Contains(dataType))
                {
                    var expected = AutoMockProvider.For(dataType);
                    var actual = rt.GetProperty("Data")!.GetValue(ret);
                    if (!object.Equals(actual, expected))
                    {
                        throw new InvalidOperationException($"Operation returned Outcome.Data '{actual}' on valid input — expected '{expected}' per the registered TestFixtures fixture for '{dataType.FullName}'.");
                    }
                }
            }
        }
