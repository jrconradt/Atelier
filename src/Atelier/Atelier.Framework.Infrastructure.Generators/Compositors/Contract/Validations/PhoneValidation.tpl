            if ({{ propertyName }} != null && !global::System.Text.RegularExpressions.Regex.IsMatch({{ propertyName }}, @"^\+?[1-9]\d{1,14}$"))
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' must be a valid phone number", new[] { "{{ propertyName }}" }));
            }
