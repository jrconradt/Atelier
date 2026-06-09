var certPath = Environment.GetEnvironmentVariable("{{ certPathEnv }}");
var certPassword = Environment.GetEnvironmentVariable("{{ certPasswordEnv }}");
if (!string.IsNullOrEmpty(certPath) && File.Exists(certPath))
{
    options.ListenAnyIP({{ varName }}, listenOptions =>
    {
        listenOptions.Protocols = {{ protocol }};
        listenOptions.UseHttps(certPath, certPassword);
    });
}{{ fallbackBlock }}
