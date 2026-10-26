using Atelier.Framework.Attributes;
using Atelier.Framework.Offering;

[assembly: Boutique("example-host",
                    Name = "Atelier Example Host",
                    Description = "A boutique host whose entry point is emitted at compile time")]
[assembly: BoutiqueProduct(typeof(LifecycleProbeProduct))]
