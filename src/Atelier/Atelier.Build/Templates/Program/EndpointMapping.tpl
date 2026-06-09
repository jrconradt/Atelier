{{ contextExtraction }}
{{ scopeEnforcement }}
{{ websocketSetup }}
{{ staticFiles }}
{{ rest }}
{{ grpcServices }}
app.MapHealthChecks("{{ healthPath }}", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("liveness")
});
app.MapHealthChecks("{{ readinessPath }}", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("readiness")
});
app.MapGet("/", () => Microsoft.AspNetCore.Http.Results.Redirect("{{ defaultRedirect }}"));

{{ infoEndpoint }}
{{ metricsEndpoint }}
{{ extensionsMap }}
