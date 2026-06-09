if (!System.Guid.TryParse({{ paramName }}?.ToString(), out _)) throw new ArgumentException("Parameter '{{ paramName }}' must be a valid GUID");
