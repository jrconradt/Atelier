            if ({{ propertyName }} != null)
            {
                var digits = {{ propertyName }}.Replace(" ", "").Replace("-", "");
                if (!global::System.Text.RegularExpressions.Regex.IsMatch(digits, @"^\d{13,19}$"))
                {
                    results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Property '{{ propertyName }}' must be a valid credit card number", new[] { "{{ propertyName }}" }));
                }
            }
