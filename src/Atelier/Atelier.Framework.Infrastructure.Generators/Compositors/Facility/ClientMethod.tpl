public async {{ returnType }} {{ methodName }}({{ parameters }})
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "api/{{ lowerFacility }}/{{ lowerMethodName }}");
    {{ requestContentCode }}
    ApplyAuthorization(request);

    using var response = await _httpClient.SendAsync(request, {{ ctParam }}).ConfigureAwait(false);
    if (!response.IsSuccessStatusCode)
    {
        return {{ unwrappedReturnType }}.Failure();
    }

    var result = await response.Content.ReadFromJsonAsync<{{ unwrappedReturnType }}>({{ ctParam }}).ConfigureAwait(false);
    return result;
}
