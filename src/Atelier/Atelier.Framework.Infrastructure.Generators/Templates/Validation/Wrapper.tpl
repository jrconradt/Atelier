#nullable enable annotations
{{ usings }}

namespace {{ namespaceName }}
{
    partial class {{ className }}{{ typeParams }}
    {
        public {{ returnType }} {{ methodName }}_Validated({{ parameterList }})
        {
{{ parameterValidations }}

            {{ returnStatement }}
        }
    }
}
