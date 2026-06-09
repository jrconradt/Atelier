            if ({{ propertyName }} != null && {{ propertyName }}.Length > {{ maxLength }})
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' must be at most {{ maxLength }} characters", new[] { "{{ propertyName }}" }));
            }
