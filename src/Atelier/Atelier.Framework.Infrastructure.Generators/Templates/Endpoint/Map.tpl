endpoints.Map{{ httpMethod }}("{{ fullRoute }}", async (HttpContext context{{ routeParamString }}) =>
{
{{ authGuard }}
    var service = context.RequestServices.GetRequiredService<{{ serviceType }}>();
    var result = {{ serviceCall }};
{{ responseCode }}
}).WithMetadata(new global::Atelier.Framework.Network.Middleware.ScopeEnforcedOperation(typeof({{ serviceType }}).GetMethod("{{ operationMethod }}")!));
