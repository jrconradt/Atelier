public {{? isAsync }}async {{?}}{{ returnType }} {{ methodName }}({{ parameters }})
{
    {{? isOutcomeOfT }}
    return await ForwardAsync(
        nameof({{ methodName }}),
        {{? hasContractValidator }}response => response.Data is not null && !global::{{ contractNamespace }}.{{ contractName }}ContractValidationExtensions.IsValid(response.Data) ? global::Atelier.Framework.Outcomes.Outcome<global::{{ contractNamespace }}.{{ contractName }}>.Failure() : response{{?else}}response => response{{?}},
        () => _backend.{{ methodName }}({{ arguments }}));
    {{?else}}
    {{? isBareOutcome }}
    return await ForwardAsync(
        nameof({{ methodName }}),
        () => _backend.{{ methodName }}({{ arguments }}));
    {{?else}}
    return _backend.{{ methodName }}({{ arguments }});
    {{?}}
    {{?}}
}
