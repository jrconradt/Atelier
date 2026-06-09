if (enableHttps || (File.Exists("{{ certPath }}") && File.Exists("{{ keyPath }}")))
{
    options.ListenAnyIP({{ varName }}, listenOptions =>
    {
        listenOptions.Protocols = {{ protocol }};
        if (File.Exists("{{ certPath }}") && File.Exists("{{ keyPath }}"))
        {
            listenOptions.UseHttps(System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile("{{ certPath }}", "{{ keyPath }}"));
        }
        else if (enableHttps)
        {
            listenOptions.UseHttps();
        }
    });
}{{ fallbackBlock }}
