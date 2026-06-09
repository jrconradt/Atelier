    var {{ camelName }}Field = type.GetField("{{ fieldName }}");
    if ({{ camelName }}Field != null)
    {
        var {{ camelName }}Value = {{ camelName }}Field.GetValue(spec);
        if ({{ camelName }}Value != null)
        {
            instance.{{ fieldName }} = ({{ fieldType }}){{ camelName }}Value;
        }
    }
