var extensionsTypeForServices = typeof(Atelier.Host.{{ boutiqueName }}.ProgramExtensions);
var configureMethod = extensionsTypeForServices.GetMethod("ConfigureServices", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
if (configureMethod != null)
{
    configureMethod.Invoke(null, new object[] { builder.Services });
}
