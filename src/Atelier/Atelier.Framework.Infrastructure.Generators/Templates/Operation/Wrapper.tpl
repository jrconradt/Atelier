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

    var __accessor = this.ContextAccessor;
    var __previousContext = __accessor?.Current;
    if (__accessor is not null)
    {
        var __operationContext = new global::Atelier.Framework.Context.CompositeContext(__previousContext);
        if (__previousContext is global::Atelier.Framework.Context.Context __parentContext)
        {
            __parentContext.PropagateInheritableState(__operationContext);
        }
        __accessor.SetCurrent(__operationContext);
    }

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
    finally
    {
        if (__accessor is not null
            && __previousContext is not null)
        {
            __accessor.SetCurrent(__previousContext);
        }
    }
}
