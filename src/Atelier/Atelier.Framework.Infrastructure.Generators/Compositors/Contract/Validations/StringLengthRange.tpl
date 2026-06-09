            if ({{ propertyName }} != null && ({{ propertyName }}.Length < {{ minLength }} || {{ propertyName }}.Length > {{ maxLength }}))
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' must be between {{ minLength }} and {{ maxLength }} characters", new[] { "{{ propertyName }}" }));
            }
