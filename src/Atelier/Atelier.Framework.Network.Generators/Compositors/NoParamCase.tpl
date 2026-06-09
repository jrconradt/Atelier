case "{{ methodName }}":
{
    {{ authorizationGuard }}
    var response = await _implementation.{{ methodName }}(cancellationToken);
    {{ returnHandling }}
}
