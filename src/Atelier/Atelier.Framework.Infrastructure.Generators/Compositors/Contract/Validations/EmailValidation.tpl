            if ({{ propertyName }} != null && !global::System.Text.RegularExpressions.Regex.IsMatch({{ propertyName }}, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' must be a valid email address", new[] { "{{ propertyName }}" }));
            }
