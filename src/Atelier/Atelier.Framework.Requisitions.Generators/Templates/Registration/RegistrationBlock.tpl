services.AddSingleton<IFactory<{{ fullTypeName }}>, {{ typeName }}Factory>();
services.{{ lifecycleMethod }}<{{ fullTypeName }}>(sp =>
{
    var factory = sp.GetRequiredService<IFactory<{{ fullTypeName }}>>();
    return factory.Create(sp);
});
