var {{ varName }} = int.TryParse(Environment.GetEnvironmentVariable("{{ envVar }}"), out var p{{ name }}) ? p{{ name }} : {{ port }};
