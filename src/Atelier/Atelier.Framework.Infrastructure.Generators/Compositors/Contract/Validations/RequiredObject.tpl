            if ({{ propertyName }} == null)
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' is required", new[] { "{{ propertyName }}" }));
            }
