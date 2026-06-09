return specification switch
    {
        {{ typeName }} typed => typed,
        { } spec => CreateFromSpecification(spec),
        _ => throw new ArgumentException("{{ typeName }} requires specification")
    };
