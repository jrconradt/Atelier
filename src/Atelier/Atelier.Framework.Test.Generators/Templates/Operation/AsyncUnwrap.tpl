if (ret is Task t)
{
    await t.ConfigureAwait(false);
    ret = ReadTaskResult(t);
}
else if (ret is ValueTask vt)
{
    await vt.ConfigureAwait(false);
    ret = null;
}
else if (ret is not null)
{
    var asTaskMi = ret.GetType().GetMethod("AsTask");
    if (asTaskMi is not null)
    {
        var taskObj = asTaskMi.Invoke(ret, null) as Task;
        if (taskObj is not null)
        {
            await taskObj.ConfigureAwait(false);
            ret = ReadTaskResult(taskObj);
        }
    }
}
