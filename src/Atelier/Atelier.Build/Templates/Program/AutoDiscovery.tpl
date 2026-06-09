builder.Services.RegisterDiscoveredServices(options =>
{
    options.ThrowOnLoadErrors = false;
    options.BeforeRegistration = context =>
    {
        if (context.ImplementationType.IsGenericTypeDefinition)
        {
            return false;
        }
        return true;
    };
});
