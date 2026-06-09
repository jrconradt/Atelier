    var {{ camelName }}Property = type.GetProperty("{{ memberName }}");
    if ({{ camelName }}Property != null)
    {
        var {{ camelName }}Value = {{ camelName }}Property.GetValue(spec);
        if ({{ camelName }}Value != null)
        {
            instance.{{ memberName }} = ({{ memberType }}){{ camelName }}Value;
        }
    }
