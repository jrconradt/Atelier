        [GeneratedTest("IAtelier/Observe-Surface-Present", "{{ target }}")]
        public static void Test_IAtelier_ObserveSurface_{{ className }}()
        {
            var type = typeof({{ fqn }});
            var observe = type.GetMethod("Observe", BindingFlags.Public | BindingFlags.Instance);
            if (observe is null)
            {
                throw new InvalidOperationException($"IAtelier-implementing class {type.FullName} has no public Observe method (generator did not emit)");
            }
            var ps = observe.GetParameters();
            if (ps.Length != 4)
            {
                throw new InvalidOperationException($"Observe must have 4 parameters (LogLevel, Exception?, string?, ReadOnlySpan<(string,object)>), got {ps.Length}");
            }
        }
