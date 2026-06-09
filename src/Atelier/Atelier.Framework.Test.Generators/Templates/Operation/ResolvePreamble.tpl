var type = typeof({{ fqn }});
Func<object> newReceiver;
if (TestFixtures.HasReceiver(type, "{{ opName }}"))
{
    newReceiver = () =>
    {
        TestFixtures.TryCreateReceiver(type, "{{ opName }}", out var fixtureInstance);
        return fixtureInstance;
    };
}
else
{
    var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(c => c.GetParameters().Length == {{ arity }});
    if (ctor is null) throw new NeedsFixtureException("No synthesized ctor available");
    newReceiver = () =>
    {
        var ctorArgs = ctor.GetParameters().Select(cp => AutoMockProvider.For(cp.ParameterType)).ToArray();
        return ctor.Invoke(ctorArgs);
    };
}
var instance = newReceiver();

var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(mi => mi.Name == "{{ opName }}_Traced" && mi.GetParameters().Length == {{ paramCount }})
          ?? type.GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(mi => mi.Name == "{{ opName }}_Validated" && mi.GetParameters().Length == {{ paramCount }})
          ?? type.GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(mi => mi.Name == "{{ opName }}" && mi.GetParameters().Length == {{ paramCount }});
if (method is null) throw new InvalidOperationException("Method not found via reflection");
