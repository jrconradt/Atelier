    var {{ camelName }}Property = type.GetProperty("{{ paramName }}");
    var {{ camelName }}Value = {{ camelName }}Property?.GetValue(spec);
    if ({{ camelName }}Value == null)
        throw new ArgumentException("Required parameter '{{ paramName }}' not found in specification");
