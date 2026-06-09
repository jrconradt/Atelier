var boutiqueMode = "{{ modeName }}";
var instanceId = Environment.GetEnvironmentVariable("INSTANCE_ID") ?? Guid.NewGuid().ToString("N")[..8];

Atelier.Framework.Observability.ApplicationMetrics.Initialize(instanceId, boutiqueMode);

Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║   ATELIER HOST - {boutiqueMode.ToUpperInvariant()} BOUTIQUE                           ║");
Console.WriteLine($"║   Instance: {instanceId,-49} ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
Console.WriteLine();
