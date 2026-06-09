case "{{ methodName }}":
{
    {{ authorizationGuard }}
    var request = _codec.Deserialize<{{ paramType }}>(message.Payload);
    if (request == null)
    {
        return Outcome.Failure();
    }
    var response = await _implementation.{{ methodName }}(request, cancellationToken);
    {{ returnHandling }}
}
