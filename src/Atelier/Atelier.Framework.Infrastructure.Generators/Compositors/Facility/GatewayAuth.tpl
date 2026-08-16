protected override global::System.Threading.Tasks.Task<global::Atelier.Framework.Outcomes.Outcome> AuthorizeAsync()
{
    if (!Context.TryGetValue("Authorization", out var header) || string.IsNullOrWhiteSpace(header))
    {
        return global::System.Threading.Tasks.Task.FromResult(global::Atelier.Framework.Outcomes.Outcome.Failure());
    }

    var token = header.StartsWith("Bearer ", global::System.StringComparison.OrdinalIgnoreCase) ? header.Substring(7) : header;
    var validation = _tokenValidator.Validate(token);
    if (!validation.IsSuccess)
    {
        return global::System.Threading.Tasks.Task.FromResult(global::Atelier.Framework.Outcomes.Outcome.Failure());
    }

    {{ claimChecks }}

    {{ scopeChecks }}

    ApplyPrincipal(validation.Data!);

    return global::System.Threading.Tasks.Task.FromResult(global::Atelier.Framework.Outcomes.Outcome.Success());
}
