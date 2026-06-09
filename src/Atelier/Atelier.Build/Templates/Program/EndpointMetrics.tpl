app.MapGet("/metrics", async (HttpContext httpContext, IServiceProvider sp) =>
{
    var acceptHeader = httpContext.Request.Headers.Accept.ToString();

    if (acceptHeader.Contains("text/plain") || acceptHeader.Contains("application/openmetrics-text") || httpContext.Request.Query.ContainsKey("prometheus"))
    {
        httpContext.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";

        using var stream = new System.IO.MemoryStream();
        await Prometheus.Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream).ConfigureAwait(false);
        stream.Position = 0;
        using var reader = new System.IO.StreamReader(stream);
        await httpContext.Response.WriteAsync(await reader.ReadToEndAsync()).ConfigureAwait(false);
        return;
    }

    var host = sp.GetRequiredService<AttacheHost>();
    var healthReport = await host.GetHealthReportAsync().ConfigureAwait(false);
    if (!healthReport.IsSuccess)
    {
        httpContext.Response.StatusCode = 500;
        return;
    }

    await httpContext.Response.WriteAsJsonAsync(healthReport.Data).ConfigureAwait(false);
});
