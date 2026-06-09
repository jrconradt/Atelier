            if ({{ propertyName }} != null && !global::System.Text.RegularExpressions.Regex.IsMatch({{ propertyName }}, {{ pattern }}))
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' has an invalid format", new[] { "{{ propertyName }}" }));
            }
