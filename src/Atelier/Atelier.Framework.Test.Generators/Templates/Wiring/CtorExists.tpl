        [GeneratedTest("DI-Wiring/Ctor-Exists", "{{ target }}")]
        public static void Test_DiWiring_CtorExists_{{ className }}()
        {
            var type = typeof({{ fqn }});
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (!ctors.Any(c => c.GetParameters().Length == {{ arity }}))
                throw new InvalidOperationException($"No public ctor with {{ arity }} parameter(s) on {type.FullName} — generator did not emit synthesized constructor (is the class partial?)");
        }
