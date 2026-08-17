public {{ asyncKeyword }}{{ returnType }} {{ wrapperName }}{{ typeParams }}({{ parameterList }})
{
    const string __opName = {{ operationName }};

    try
    {
        this.Logger?
            .WithMessage("Executing operation")
            .WithValue("OperationName", __opName)
            .WithValue("Method", "{{ methodName }}")
            .WithLevel(global::Atelier.Framework.Observability.LogLevel.Debug)
            .Log();
    }
    catch { }

    try
    {
        {{ returnKeyword }}{{ awaitKeyword }}{{ methodName }}{{ typeArgs }}({{ argumentList }});
    }
    catch (global::System.Exception ex)
    {
        try
        {
            this.Logger?
                .WithMessage("Operation failed")
                .WithValue("OperationName", __opName)
                .WithValue("Method", "{{ methodName }}")
                .WithError(ex)
                .WithLevel(global::Atelier.Framework.Observability.LogLevel.Error)
                .Log();
        }
        catch { }

        {{ failureReturn }}
    }
}
