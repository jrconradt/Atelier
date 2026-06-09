app.MapControllers();
var restGroup = app.MapGroup("{{ basePath }}");
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
{
    if (asm.IsDynamic)
    {
        continue;
    }
    foreach (var t in asm.GetTypes())
    {
        if (!t.IsClass || !t.IsAbstract || !t.IsSealed)
        {
            continue;
        }
        if (!t.Name.EndsWith("ApiEndpoints", System.StringComparison.Ordinal))
        {
            continue;
        }
        var m = t.GetMethod("Map" + t.Name.Substring(0, t.Name.Length - "ApiEndpoints".Length) + "Endpoints", new[] { typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder) });
        if (m != null)
        {
            m.Invoke(null, new object[] { restGroup });
        }
    }
}
