app.MapGet("/info", () => Microsoft.AspNetCore.Http.Results.Ok(new
{
    boutique = new
    {
        id = "{{ boutiqueId }}",
        name = "{{ name }}",
        version = "{{ version }}",
        mode = boutiqueMode,
        instance = instanceId
    },
    capabilities = manifest.Capabilities,
    products = manifest.Products.Select(p => new { type = p.ProductType?.Name ?? p.ProductTypeName, autoStart = p.AutoStart }),
    timestamp = DateTime.UtcNow
}));
