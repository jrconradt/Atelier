            if ({{ propertyName }} != null && !global::System.Uri.TryCreate({{ propertyName }}, global::System.UriKind.Absolute, out _))
            {
                results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' must be a valid URL", new[] { "{{ propertyName }}" }));
            }
