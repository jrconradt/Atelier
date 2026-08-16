var grantedScopes = validation.Data!.Claims
    .Where(entry => entry.Type == "scope" || entry.Type == "scp")
    .SelectMany(entry => entry.Value.Split(' ', global::System.StringSplitOptions.RemoveEmptyEntries))
    .ToHashSet();

{{ scopeAssertions }}
