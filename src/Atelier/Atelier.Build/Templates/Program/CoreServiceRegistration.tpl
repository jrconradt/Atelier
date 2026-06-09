builder.Services.AddSingleton<IContext>(sp => global::Atelier.Framework.Context.Context.Empty);
builder.Services.AddSingleton<IContextAccessor, DefaultContextAccessor>();
builder.Services.AddSingleton<ILoggingStrategy, ConsoleLoggingStrategy>();
builder.Services.AddSingleton<Atelier.Framework.Observability.ILogger>(sp => new Logger(
    sp.GetRequiredService<IContextAccessor>(),
    sp.GetRequiredService<ILoggingStrategy>()));
