typeof({{ declaringTypeName }}).GetField("{{ memberName }}", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(this, {{ paramName }});
