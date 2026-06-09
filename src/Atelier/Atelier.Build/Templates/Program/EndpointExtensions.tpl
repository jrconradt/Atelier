var extensionsType = typeof(Atelier.Host.{{ boutiqueName }}.ProgramExtensions);
var mapMethod = extensionsType.GetMethod("MapCustomEndpoints", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
if (mapMethod != null)
{
    mapMethod.Invoke(null, new object[] { app });
}
