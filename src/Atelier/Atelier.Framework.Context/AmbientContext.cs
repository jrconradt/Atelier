namespace Atelier.Framework.Context;

public static class AmbientContext
{
    private static readonly AsyncLocal<IContext?> Slot = new();

    public static IContext Current
    {
        get
        {
            if (Slot.Value is not null)
            {
                return Slot.Value;
            }

            var systemContext = Context.CreateSystemContext("AmbientOperation");
            Slot.Value = systemContext;
            return systemContext;
        }
    }

    public static void SetCurrent(IContext context)
    {
        Slot.Value = context;
    }

    public static string? CurrentUserId => Current.Authorization?.UserId;

    public static string? CurrentTenantId => Current.Authorization?.TenantId;
}
