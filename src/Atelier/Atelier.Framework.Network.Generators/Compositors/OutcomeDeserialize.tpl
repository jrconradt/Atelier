try
{
    var response = await _transport.SendAsync(message, cancellationToken);
    if (response is null)
    {
        return {{ argType }}.Failure();
    }
    if (response.Headers.ContainsKey(global::Atelier.Framework.Network.Transport.TransportMessage.RESPONSE_ERROR_CODE_HEADER))
    {
        return {{ argType }}.Failure();
    }
    if (response.Payload != null && response.Payload.Length > 0)
    {
        var deserialized = _codec.Deserialize<{{ argType }}>(response.Payload);
        return deserialized;
    }
    return {{ argType }}.Failure();
}
catch (global::System.Exception)
{
    return {{ argType }}.Failure();
}
