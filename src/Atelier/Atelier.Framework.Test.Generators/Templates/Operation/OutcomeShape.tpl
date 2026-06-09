private static bool IsAtelierOutcome(global::System.Type t)
{
    var fullName = t.IsGenericType
        ? t.GetGenericTypeDefinition().FullName
        : t.FullName;
    if (fullName == "Atelier.Framework.Outcomes.Outcome")
    {
        return true;
    }
    if (fullName == "Atelier.Framework.Outcomes.Outcome`1")
    {
        return true;
    }
    return false;
}

private static object? ReadTaskResult(global::System.Threading.Tasks.Task task)
{
    var cursor = task.GetType();
    while (cursor is not null)
    {
        if (cursor.IsGenericType && cursor.GetGenericTypeDefinition() == typeof(global::System.Threading.Tasks.Task<>))
        {
            return cursor.GetProperty("Result")!.GetValue(task);
        }
        cursor = cursor.BaseType;
    }
    return null;
}
