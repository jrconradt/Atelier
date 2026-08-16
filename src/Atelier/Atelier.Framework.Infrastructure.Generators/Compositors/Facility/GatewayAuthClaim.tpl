if (!validation.Data!.Claims.Any(claim => claim.Type == "{{ claim }}"))
{
    return global::System.Threading.Tasks.Task.FromResult(global::Atelier.Framework.Outcomes.Outcome.Failure());
}
