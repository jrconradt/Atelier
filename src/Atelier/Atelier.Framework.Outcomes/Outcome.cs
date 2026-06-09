using System.Diagnostics.CodeAnalysis;

namespace Atelier.Framework.Outcomes;

public struct Outcome
{
    public bool IsSuccess { get; }

    private Outcome(bool isSuccess)
    {
        IsSuccess = isSuccess;
    }

    public static Outcome Success() =>
        new Outcome(true);

    public static Outcome Failure() =>
        new Outcome(false);

    public static implicit operator Outcome(bool success)
        => success ? Success() : Failure();

    public static bool operator ==(Outcome left, Outcome right)
        => left.IsSuccess == right.IsSuccess;

    public static bool operator !=(Outcome left, Outcome right)
        => !(left == right);

    public override bool Equals(object? obj)
        => obj is Outcome other && this == other;

    public override int GetHashCode()
        => HashCode.Combine(IsSuccess);
}

public interface IOutcome<T>
{
    public T? Data { get; }
    public bool IsSuccess { get; }
    public abstract static Outcome<T> Failure();
    public abstract static Outcome<T> Success(T data);
    public void Deconstruct(out T? data, out bool isSuccess);
}

public struct Outcome<T> : IOutcome<T>
{
    private readonly bool _initialized = false;

    public T? Data { get; }
    [MemberNotNullWhen(true, nameof(Data))]
    public bool IsSuccess { get; } = false;

    public bool IsDefault => !_initialized;

    public Outcome()
    {
        Data = default!;
        IsSuccess = false;
        _initialized = true;
    }

    public Outcome(T data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        Data = data;
        IsSuccess = true;
        _initialized = true;
    }

    public static Outcome<T> Success(T data) =>
        new Outcome<T>(data);

    public static Outcome<T> Failure() =>
        new Outcome<T>();

    public void Deconstruct(
        out T? data,
        out bool isSuccess)
    {
        data = Data;
        isSuccess = IsSuccess;
    }

    public static implicit operator Outcome<T>(T data)
        => Success(data);

    public static bool operator ==(Outcome<T> left, Outcome<T> right)
        => left.IsSuccess == right.IsSuccess
           && EqualityComparer<T>.Default.Equals(left.Data, right.Data);

    public static bool operator !=(Outcome<T> left, Outcome<T> right)
        => !(left == right);

    public override bool Equals(object? obj)
        => obj is Outcome<T> other && this == other;

    public override int GetHashCode()
        => HashCode.Combine(IsSuccess, Data);
}

public static class OutcomeExtensions
{
    public static T? Value<T>(this Outcome<T> outcome) => outcome.Data;

    public static bool IsFailure(this Outcome outcome) => !outcome.IsSuccess;

    public static bool IsFailure<T>(this Outcome<T> outcome) => !outcome.IsSuccess;

    public static Outcome ToOutcome(this bool success)
        => success ? Outcome.Success() : Outcome.Failure();

    public static Task<Outcome> ToOutcomeTask(this bool success)
        => Task.FromResult(success.ToOutcome());

    public static Outcome<string> ToOutcome(this string value)
        => Outcome<string>.Success(value);

    public static Task<Outcome<string>> ToOutcomeTask(this string value)
        => Task.FromResult(value.ToOutcome());

    public static Outcome<List<TItems>> ToOutcome<TItems>(this List<TItems> value)
        where TItems : notnull
        => Outcome<List<TItems>>.Success(value);

    public static Task<Outcome<List<TItems>>> ToOutcomeTask<TItems>(this List<TItems> value)
        where TItems : notnull
        => Task.FromResult(value.ToOutcome());

    public static Outcome<T> ToOutcome<T>(this Exception exception)
        where T : notnull
        => Outcome<T>.Failure();

    public static Task<Outcome<T>> ToOutcomeTask<T>(this Exception exception)
        where T : notnull
        => Task.FromResult(exception.ToOutcome<T>());

    public static Outcome ToOutcome(this Exception exception)
        => Outcome.Failure();

    public static Task<Outcome> ToOutcomeTask(this Exception exception)
        => Task.FromResult(exception.ToOutcome());

    public static Task<Outcome<T>> ToOutcomeTask<T>(this T value)
        where T : notnull
        => Task.FromResult<Outcome<T>>(value);

    public static Task<Outcome> ToOutcomeTask(this Outcome outcome)
        => Task.FromResult(outcome);

    public static Outcome<U> Bind<T, U>(this Outcome<T> outcome,
                                        Func<T, Outcome<U>> next)
    {
        if (!outcome.IsSuccess)
        {
            return Outcome<U>.Failure();
        }

        return next(outcome.Data);
    }

    public static Outcome<U> Map<T, U>(this Outcome<T> outcome,
                                       Func<T, U> selector)
    {
        if (!outcome.IsSuccess)
        {
            return Outcome<U>.Failure();
        }

        return Outcome<U>.Success(selector(outcome.Data));
    }

    public static R Match<T, R>(this Outcome<T> outcome,
                                Func<T, R> onSuccess,
                                Func<R> onFailure)
    {
        if (outcome.IsSuccess)
        {
            return onSuccess(outcome.Data);
        }

        return onFailure();
    }

    public static R Match<R>(this Outcome outcome,
                             Func<R> onSuccess,
                             Func<R> onFailure)
    {
        if (outcome.IsSuccess)
        {
            return onSuccess();
        }

        return onFailure();
    }

    public static Outcome<T> Tap<T>(this Outcome<T> outcome,
                                    Action<T> onSuccess)
    {
        if (outcome.IsSuccess)
        {
            onSuccess(outcome.Data);
        }

        return outcome;
    }

    public static Outcome<T> OnFailure<T>(this Outcome<T> outcome,
                                          Action handler)
    {
        if (!outcome.IsSuccess)
        {
            handler();
        }

        return outcome;
    }

    public static Outcome OnFailure(this Outcome outcome,
                                    Action handler)
    {
        if (!outcome.IsSuccess)
        {
            handler();
        }

        return outcome;
    }

    public static Outcome<U> TunnelFailure<T, U>(this Outcome<T> outcome)
        => Outcome<U>.Failure();

    public static Outcome<U> TunnelFailure<U>(this Outcome outcome)
        => Outcome<U>.Failure();

    public static async Task<Outcome<U>> BindAsync<T, U>(this Outcome<T> outcome,
                                                         Func<T, Task<Outcome<U>>> next)
    {
        if (!outcome.IsSuccess)
        {
            return Outcome<U>.Failure();
        }

        return await next(outcome.Data).ConfigureAwait(false);
    }

    public static async Task<Outcome<U>> BindAsync<T, U>(this Task<Outcome<T>> outcomeTask,
                                                         Func<T, Outcome<U>> next)
    {
        var outcome = await outcomeTask.ConfigureAwait(false);
        return outcome.Bind(next);
    }

    public static async Task<Outcome<U>> BindAsync<T, U>(this Task<Outcome<T>> outcomeTask,
                                                         Func<T, Task<Outcome<U>>> next)
    {
        var outcome = await outcomeTask.ConfigureAwait(false);
        return await outcome.BindAsync(next).ConfigureAwait(false);
    }

    public static async Task<Outcome<U>> MapAsync<T, U>(this Task<Outcome<T>> outcomeTask,
                                                        Func<T, U> selector)
    {
        var outcome = await outcomeTask.ConfigureAwait(false);
        return outcome.Map(selector);
    }
}
