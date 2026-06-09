var instance = specification switch
    {
        {{ typeName }} typed => typed,
        { } spec => MapFromSpecification(spec),
        _ => throw new ArgumentException("{{ typeName }} requires specification")
    };
