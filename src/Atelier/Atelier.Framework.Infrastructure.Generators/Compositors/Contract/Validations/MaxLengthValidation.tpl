            if ({{ propertyName }} != null && {{ propertyName }}.{{ lengthProperty }} > {{ maxLength }})
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' must be at most {{ maxLength }} items", new[] { "{{ propertyName }}" }));
            }
