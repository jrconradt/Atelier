            if ({{ propertyName }} != null && {{ propertyName }}.{{ lengthProperty }} < {{ minLength }})
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' must be at least {{ minLength }} items", new[] { "{{ propertyName }}" }));
            }
