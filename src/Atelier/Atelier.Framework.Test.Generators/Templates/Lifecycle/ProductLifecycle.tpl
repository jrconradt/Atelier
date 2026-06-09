        private sealed class AutoMockedOfferingProvider_{{ className }} : global::Atelier.Framework.Offering.IOfferingProvider
        {
            public TOffering? GetOffering<TOffering>() where TOffering : class
                => AutoMockProvider.For(typeof(global::Atelier.Framework.Offering.IOffering)) as TOffering;

            public global::Atelier.Framework.Outcomes.Outcome<TOffering> GetRequiredOffering<TOffering>() where TOffering : class
                => global::Atelier.Framework.Outcomes.Outcome<TOffering>.Failure();

            public global::System.Collections.Generic.IEnumerable<TOffering> GetOfferings<TOffering>() where TOffering : class
                => global::System.Array.Empty<TOffering>();

            public object? GetOffering(global::System.Type offeringType)
                => AutoMockProvider.For(typeof(global::Atelier.Framework.Offering.IOffering));

            public global::Atelier.Framework.Outcomes.Outcome<object> GetRequiredOffering(global::System.Type offeringType)
                => global::Atelier.Framework.Outcomes.Outcome<object>.Success(AutoMockProvider.For(typeof(global::Atelier.Framework.Offering.IOffering))!);
        }

        [GeneratedTest("Lifecycle/Product-Configure-Start-Stop-Succeeds", "{{ target }}")]
        public static async Task Test_Lifecycle_ConfigureStartStop_{{ className }}()
        {
            var type = typeof({{ fqn }});
            var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           .FirstOrDefault(c => c.GetParameters().Length == {{ arity }});
            if (ctor is null)
            {
                throw new NeedsFixtureException($"Product {type.FullName} has no synthesized constructor of arity {{ arity }}; register a TestFixtures fixture supplying a constructed product.");
            }

            var provider = new AutoMockedOfferingProvider_{{ className }}();
            var parameters = ctor.GetParameters();
            var ctorArgs = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                if (typeof(global::Atelier.Framework.Offering.IOfferingProvider).IsAssignableFrom(parameters[i].ParameterType))
                {
                    ctorArgs[i] = provider;
                }
                else
                {
                    ctorArgs[i] = AutoMockProvider.For(parameters[i].ParameterType);
                }
            }

            var product = ctor.Invoke(ctorArgs) as global::Atelier.Framework.Offering.Product.ProductBase;
            if (product is null)
            {
                throw new InvalidOperationException($"Could not instantiate product {type.FullName} via its synthesized constructor");
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            var configuration = new global::Atelier.Framework.Offering.Product.Configuration.OfferingConfiguration(provider);
            var configure = type.GetMethod("ConfigureOfferings", flags);
            if (configure is null)
            {
                throw new InvalidOperationException($"Product {type.FullName} has no ConfigureOfferings method");
            }
            configure.Invoke(product, new object?[] { configuration });
            if (configuration.GetOfferingTypes().Count == 0)
            {
                throw new InvalidOperationException($"Product {type.FullName} registered no offerings in ConfigureOfferings");
            }

            if (product.IsRunning)
            {
                throw new InvalidOperationException($"Product {type.FullName} reported running before StartAsync");
            }

            var started = await product.StartAsync().ConfigureAwait(false);
            if (!started.IsSuccess)
            {
                throw new InvalidOperationException($"StartAsync returned Outcome.Failure on {type.FullName}");
            }
            if (!product.IsRunning)
            {
                throw new InvalidOperationException($"Product {type.FullName} did not report running after StartAsync");
            }

            var stopped = await product.StopAsync().ConfigureAwait(false);
            if (!stopped.IsSuccess)
            {
                throw new InvalidOperationException($"StopAsync returned Outcome.Failure on {type.FullName}");
            }
            if (product.IsRunning)
            {
                throw new InvalidOperationException($"Product {type.FullName} still reported running after StopAsync");
            }
        }
