        public static {{ asyncKeyword }}{{ methodReturnType }} {{ methodName }}_Validated(
            {{ className }} service,
            {{ parameterList }})
        {

            {{ validations }}

            {{ serviceCall }}
        }
