if (!System.Text.RegularExpressions.Regex.IsMatch({{ paramName }} ?? "", @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) throw new ArgumentException("Parameter '{{ paramName }}' must be a valid email address");
