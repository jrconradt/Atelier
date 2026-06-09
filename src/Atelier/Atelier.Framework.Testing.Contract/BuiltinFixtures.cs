namespace Atelier.Framework.Testing;

internal static class BuiltinFixtures
{
    internal static void Register()
    {
        TestFixtures.Register(() => new HttpClient());
        TestFixtures.Register(typeof(Type), () => typeof(object));
    }
}
