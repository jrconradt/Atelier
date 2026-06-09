var attacheHost = app.Services.GetRequiredService<AttacheHost>();
attacheHost.Configure(new AttacheConfiguration
{
    AutoStartBoutiques = true,
    ResourceLimits = new AttacheResourceLimits
    {
        MaxBoutiques = 1,
        MaxMemoryBytes = {{ maxMemory }},
        MaxCpuPercent = {{ maxCpu }}
    }
});
