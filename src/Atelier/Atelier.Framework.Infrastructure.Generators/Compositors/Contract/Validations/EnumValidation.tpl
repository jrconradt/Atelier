            if (!global::System.Enum.IsDefined(typeof({{ enumTypeName }}), {{ propertyName }}))
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' has an invalid enum value: {0}".Replace("{0}", {{ propertyName }}.ToString()), new[] { "{{ propertyName }}" }));
            }
