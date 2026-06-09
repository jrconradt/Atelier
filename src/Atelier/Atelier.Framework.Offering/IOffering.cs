namespace Atelier.Framework.Offering;

public interface IOffering
{
    public void Start();
    public void Stop();
    public bool IsRunning { get; }
}
