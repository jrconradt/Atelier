namespace Atelier.Build.Pipeline;

public sealed class PhaseCounter
{
    private int _current;

    public int Next()
    {
        _current++;
        return _current;
    }
}
