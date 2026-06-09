var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine();
    Console.WriteLine("{{ schemaName }} started");
    Console.WriteLine($"  Instance: {instanceId}");
});

lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("Shutting down {{ schemaName }}...");
    app.Services.DrainForShutdownAsync().GetAwaiter().GetResult();
});
