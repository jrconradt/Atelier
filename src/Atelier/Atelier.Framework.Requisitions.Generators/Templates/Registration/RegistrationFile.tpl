public static class RequisitionServiceRegistration
{
    public static IServiceCollection AddRequisitionServices(this IServiceCollection services)
    {
        {{ registrations }}

        return services;
    }
}
