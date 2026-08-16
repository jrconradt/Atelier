if (!grantedScopes.Contains("{{ scope }}"))
{
    return global::System.Threading.Tasks.Task.FromResult(global::Atelier.Framework.Outcomes.Outcome.Failure());
}
