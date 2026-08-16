endpoints.MapPost("api/{{ lowerFacility }}/{{ lowerMethodName }}", async (
    {{ parameterString }}
    {{ className }} service,
    CancellationToken cancellationToken) =>
{
    {{ validationCheck }}
    var response = await service.{{ methodName }}({{ serviceArgs }});
    return response.IsSuccess ? Results.Ok(response) : Results.BadRequest(response);
});
