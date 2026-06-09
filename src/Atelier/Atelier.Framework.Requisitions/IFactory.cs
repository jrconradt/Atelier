namespace Atelier.Framework.Requisitions;

public interface IFactory<T> where T : class
{
    public T Create(
        IServiceProvider serviceProvider,
        object? specification = null);

    public void Return(T instance);

    public void Reset();
}
