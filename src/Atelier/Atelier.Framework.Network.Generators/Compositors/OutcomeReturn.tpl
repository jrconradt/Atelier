if (!response.IsSuccess)
{
    return Outcome.Failure();
}
message.Payload = _codec.Serialize(response);
return Outcome.Success();
