    public async {{ returnType }} {{ methodName }}({{ parameterList }})
    {
        {{ payloadInit }}

        var message = new TransportMessage
        {
            MessageType = "{{ methodName }}",
            Payload = payload
        };
        message.SetHeader("Content-Type", _codec.ContentType);

        {{ deserializeBlock }}
    }
