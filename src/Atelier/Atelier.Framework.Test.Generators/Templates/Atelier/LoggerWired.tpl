        [GeneratedTest("DI-Wiring/Logger-Wired", "{{ target }}")]
        public static void Test_DiWiring_LoggerWired_{{ className }}()
        {
            var type = typeof({{ fqn }});
            var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(c => c.GetParameters().Length == {{ arity }});
            if (ctor is null) throw new NeedsFixtureException("No synthesized ctor available");
            var ctorParams = ctor.GetParameters();
            var args = new object?[ctorParams.Length];
            for (var i = 0; i < ctorParams.Length; i++)
            {
                var cp = ctorParams[i];
                try
                {
                    args[i] = AutoMockProvider.For(cp.ParameterType);
                }
                catch (NeedsFixtureException ex)
                {
                    throw new NeedsFixtureException($"Cannot auto-mock ctor parameter '{cp.Name}' of type '{cp.ParameterType.FullName}': {ex.Message}");
                }
            }
            var instance = ctor.Invoke(args);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var f = type.GetField("Logger", flags);
            var prop = type.GetProperty("Logger", flags);
            var val = f?.GetValue(instance) ?? prop?.GetValue(instance);
            if (val is null)
                throw new InvalidOperationException($"Logger field/property is null after ctor invocation on {type.FullName} — generator did not wire the inherited Logger member");
        }
