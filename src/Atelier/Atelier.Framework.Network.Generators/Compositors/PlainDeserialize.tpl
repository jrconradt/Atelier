var response = await _transport.SendAsync(message, cancellationToken);
if (response is not null
    && response.Headers.TryGetValue(global::Atelier.Framework.Network.Transport.TransportMessage.RESPONSE_ERROR_CODE_HEADER, out var errorCode))
{
    response.Headers.TryGetValue(global::Atelier.Framework.Network.Transport.TransportMessage.RESPONSE_ERROR_MESSAGE_HEADER, out var errorMessage);
    throw new InvalidOperationException($"Transport request failed ({errorCode}): {errorMessage}");
}
if (response?.Payload != null
    && response.Payload.Length > 0)
{
    var deserialized = _codec.Deserialize<{{ argType }}>(response.Payload);
    return deserialized!;
}
throw new InvalidOperationException("No response received from transport");
