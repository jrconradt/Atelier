        [GeneratedTest("IAtelier/Logger-Surface-Present", "{{ target }}")]
        public static void Test_IAtelier_LoggerSurface_{{ className }}()
        {
            var type = typeof({{ fqn }});
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var f = type.GetField("Logger", flags);
            var p = type.GetProperty("Logger", flags);
            if (f is null && p is null)
                throw new InvalidOperationException($"IAtelier-implementing class {type.FullName} has no Logger field/property");
        }
