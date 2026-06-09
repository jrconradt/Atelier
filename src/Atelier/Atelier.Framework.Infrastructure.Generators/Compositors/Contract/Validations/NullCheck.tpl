            if ({{ propertyName }} == null)
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' cannot be null", new[] { "{{ propertyName }}" }));
            }
