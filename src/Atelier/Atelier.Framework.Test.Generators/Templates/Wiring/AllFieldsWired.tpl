        [GeneratedTest("DI-Wiring/All-Fields-Wired", "{{ target }}")]
        public static void Test_DiWiring_AllFieldsWired_{{ className }}()
        {
            var type = typeof({{ fqn }});
            var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(c => c.GetParameters().Length == {{ arity }});
            if (ctor is null) throw new InvalidOperationException("No matching ctor — see Ctor-Exists test");
            var ctorParams = ctor.GetParameters();
            var args = new object?[ctorParams.Length];
            for (var i = 0; i < ctorParams.Length; i++)
            {
                var p = ctorParams[i];
                try
                {
                    args[i] = AutoMockProvider.For(p.ParameterType);
                }
                catch (NeedsFixtureException ex)
                {
                    throw new NeedsFixtureException($"Cannot auto-mock ctor parameter '{p.Name}' of type '{p.ParameterType.FullName}': {ex.Message}");
                }
            }
            var instance = ctor.Invoke(args);

            foreach (var fieldName in new[] {
                {{ fieldNames }}
            })
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var f = type.GetField(fieldName, flags);
                var p = type.GetProperty(fieldName, flags);
                var val = f?.GetValue(instance) ?? p?.GetValue(instance);
                if (val is null)
                    throw new InvalidOperationException($"Required field/property '{fieldName}' is null after ctor invocation on {type.FullName} — generator did not wire it");
            }
        }
