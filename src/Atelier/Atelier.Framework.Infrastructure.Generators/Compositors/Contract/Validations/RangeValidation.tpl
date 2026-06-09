            if ({{ propertyName }} < {{ min }} || {{ propertyName }} > {{ max }})
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' must be between {{ min }} and {{ max }}", new[] { "{{ propertyName }}" }));
            }
